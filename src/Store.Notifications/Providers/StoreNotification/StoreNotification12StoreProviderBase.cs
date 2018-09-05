﻿//----------------------------------------------------------------------- 
// PDS WITSMLstudio Store, 2018.3
//
// Copyright 2018 PDS Americas LLC
// 
// Licensed under the PDS Open Source WITSML Product License Agreement (the
// "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//   
//     http://www.pds.group/WITSMLstudio/OpenSource/ProductLicenseAgreement
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Energistics.DataAccess;
using Witsml200 = Energistics.DataAccess.WITSML200;
using Energistics.Etp.Common.Datatypes;
using Energistics.Etp.v12.Datatypes.Object;
using Energistics.Etp.v12.Protocol.StoreNotification;
using Newtonsoft.Json.Linq;
using PDS.WITSMLstudio.Framework;

namespace PDS.WITSMLstudio.Store.Providers.StoreNotification
{
    /// <summary>
    /// Provides a common framework for all Store Notification Store provider implementations.
    /// </summary>
    /// <seealso cref="StoreNotificationStoreHandler" />
    public abstract class StoreNotification12StoreProviderBase : StoreNotificationStoreHandler
    {
        private readonly Dictionary<string, IMessageHeader> _headers;
        private readonly List<NotificationRequest> _requests;

        /// <summary>
        /// Initializes a new instance of the <see cref="StoreNotification12StoreProviderBase"/> class.
        /// </summary>
        protected StoreNotification12StoreProviderBase()
        {
            _headers = new Dictionary<string, IMessageHeader>();
            _requests = new List<NotificationRequest>();
        }

        /// <summary>
        /// Handles the notification request.
        /// </summary>
        /// <param name="header">The header.</param>
        /// <param name="request">The request.</param>
        protected override void HandleNotificationRequest(IMessageHeader header, NotificationRequest request)
        {
            base.HandleNotificationRequest(header, request);
            EnsureConnection();

            if (_requests.Any(x => x.Request.Uuid.EqualsIgnoreCase(request.Request.Uuid)))
            {
                // TODO: Should this be an error?
            }
            else
            {
                _headers[request.Request.Uuid] = header;
                _requests.Add(request);
            }
        }

        /// <summary>
        /// Handles the cancel notification.
        /// </summary>
        /// <param name="header">The header.</param>
        /// <param name="request">The request.</param>
        protected override void HandleCancelNotification(IMessageHeader header, CancelNotification request)
        {
            base.HandleCancelNotification(header, request);

            var message = _requests.FirstOrDefault(x => x.Request.Uuid.EqualsIgnoreCase(request.RequestUuid));

            if (message == null)
            {
                // TODO: Should this be an error?
            }
            else
            {
                _requests.Remove(message);
                _headers.Remove(message.Request.Uuid);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">
        ///   <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Ensures the connection to the message broker.
        /// </summary>
        protected virtual void EnsureConnection()
        {
        }

        /// <summary>
        /// Disconnects from the message broker.
        /// </summary>
        protected virtual void Disconnect()
        {
        }

        protected virtual void OnNotifyInsert(string uri, object dataObject, DateTime dateTime)
        {
            OnNotify(ChangeNotification, uri, dataObject, dateTime, ObjectChangeKinds.insert);
        }

        protected virtual void OnNotifyUpdate(string uri, object dataObject, DateTime dateTime)
        {
            OnNotify(ChangeNotification, uri, dataObject, dateTime, ObjectChangeKinds.update);
        }

        protected virtual void OnNotifyDelete(string uri, object dataObject, DateTime dateTime)
        {
            var request = _requests.FirstOrDefault(x => x.Request.Uri.EqualsIgnoreCase(uri));
            if (request == null) return;

            IMessageHeader header;
            if (!_headers.TryGetValue(request.Request.Uuid, out header)) return;

            DeleteNotification(header, uri, dateTime.ToUnixTimeMicroseconds());
        }

        protected virtual void OnNotify(Func<IMessageHeader, ObjectChange, long> action, string uri, object dataObject, DateTime dateTime, ObjectChangeKinds changeKind)
        {
            var request = _requests.FirstOrDefault(x => x.Request.Uri.EqualsIgnoreCase(uri));
            if (request == null) return;

            IMessageHeader header;
            if (!_headers.TryGetValue(request.Request.Uuid, out header)) return;

            var etpUri = new EtpUri(uri);

            action(header, new ObjectChange
            {
                ChangeKind = changeKind,
                ChangeTime = dateTime.ToUnixTimeMicroseconds(),
                DataObject = GetDataObject(etpUri.ObjectType, etpUri.Version, dataObject, request.Request.IncludeObjectData)
            });
        }

        protected virtual DataObject GetDataObject(string objectType, string version, object dataObject, bool includeObjectData)
        {
            var jObject = dataObject as JObject;

            if (jObject != null || dataObject is string)
            {
                var type = ObjectTypes.GetObjectGroupType(objectType, version) ??
                           ObjectTypes.GetObjectType(objectType, version);

                dataObject = jObject?.ToObject(type) ??
                    WitsmlParser.Parse(type, WitsmlParser.Parse(dataObject.ToString()).Root);
            }

            var collection = dataObject as IEnergisticsCollection;
            var iDataObject = collection?.Items?.OfType<IDataObject>().FirstOrDefault();
            var cDataObject = iDataObject as ICommonDataObject;
            var aDataObject = dataObject as Witsml200.AbstractObject;

            var uri = iDataObject?.GetUri() ?? aDataObject?.GetUri() ?? new EtpUri();
            var name = iDataObject?.Name ?? aDataObject?.Citation?.Title;
            var lastChanged = cDataObject?.CommonData?.DateTimeLastChange?.ToUnixTimeMicroseconds() ??
                              aDataObject?.Citation?.LastUpdate?.ToUnixTimeMicroseconds();

            var etpDataObject = new DataObject();

            // Do not return DataObject.Data if not requested in original subscription
            Session.Adapter.SetDataObject(
                etpDataObject,
                includeObjectData ? dataObject : null,
                uri,
                name,
                -1,
                lastChanged.GetValueOrDefault());

            return etpDataObject;
        }
    }
}