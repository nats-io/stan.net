// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;

namespace STAN.Client
{
    /// <summary>
    /// Subscription represents a subscription within the NATS Streaming cluster. Subscriptions will be rate matched and follow at-least delivery semantics.
    /// </summary>
    public interface IStanSubscription : IDisposable
    {
        /// <summary>
        /// Removes interest in the given subject.
        /// For durables, it means that the durable interest is also removed
        /// from the server. Restarting a durable with the same name will 
        /// not resume the subscription, it will be considered a new one.
        /// </summary>
        void Unsubscribe();

        /// <summary>
        /// Close removes this subscriber from the server, but unlike Unsubscribe(),
        /// the durable interest is not removed. If the client has connected to a server
        /// for which this feature is not available, Close() will return a ErrNoServerSupport
        /// error.
        /// </summary>
        void Close();
    }
}
