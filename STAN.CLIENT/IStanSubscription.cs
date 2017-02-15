/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
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
