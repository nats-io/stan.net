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
using System.Threading.Tasks;

namespace STAN.Client
{
    /// <summary>
    /// A connection represents a connection to the NATS Streaming subsystem.
    /// It can publish and subscribe to messages within the NATS Streaming cluster. 
    /// </summary>
    public interface IStanConnection : IDisposable
    {
        /// <summary>
        /// Publish publishes the data argument to the given subject. The data
        /// argument is left untouched and needs to be correctly interpreted on
        /// the receiver.  This API is synchronous and waits for the acknowledgement
        /// or error from the NATS streaming server.
        /// </summary>
        /// <param name="subject">Subject to publish the message to.</param>
        /// <param name="data">Message payload.</param>
        /// <exception cref="StanException">When an error occurs locally or on the NATS streaming server.</exception>
	    void Publish(string subject, byte[] data);

        /// <summary>
        /// Publish publishes the data argument to the given subject. The data
        /// argument is left untouched and needs to be correctly interpreted on
        /// the receiver.  This API is asynchronous and handles the acknowledgement
        /// or error from the NATS streaming server in the provided handler.  An exception is thrown when
        /// an error occurs during the send, the handler will process acknowledgments and errors.
        /// </summary>
        /// <param name="subject">Subject to publish the message to.</param>
        /// <param name="data">Message payload.</param>
        /// <param name="handler">Event handler to process message acknowledgements.</param>
        /// <returns>The GUID of the published message.</returns>
        /// <exception cref="StanException">Thrown when an error occurs publishing the message.</exception>
        string Publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler);

        /// <summary>
        /// Publish publishes the data argument to the given subject. The data
        /// argument is left untouched and needs to be correctly interpreted on
        /// the receiver.  This API is asynchronous and handles the acknowledgement
        /// or error from the NATS streaming server in the provided handler.  An exception is thrown when
        /// an error occurs during the send, the handler will process acknowledgments and errors.
        /// </summary>
        /// <param name="subject">Subject to publish the message to.</param>
        /// <param name="data"></param>
        /// <returns>The task object representing the asynchronous operation, containing the guid.</returns>
        Task<string> PublishAsync(string subject, byte[] data);

        /// <summary>
        /// Subscribe will create an Asynchronous Subscriber with
        /// interest in a given subject, assign the handler, and immediately
        /// start receiving messages.  The subscriber will default options.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <param name="handler">A message handler to process messages.</param>
        /// <returns>A new Subscription</returns>
        /// <exception cref="StanException">An error occured creating the subscriber.</exception>
	    IStanSubscription Subscribe(string subject, EventHandler<StanMsgHandlerArgs> handler);

        /// <summary>
        /// Subscribe will create an Asynchronous subscriber with
        /// interest in a given subject, assign the handler, and immediately
        /// start receiving messages.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <param name="options">SubscriptionOptions used to create the subscriber.</param>
        /// <param name="handler">A message handler to process messages.</param>
        /// <returns>A new subscription.</returns>
        /// <exception cref="StanException">An error occured creating the subscriber.</exception>
        IStanSubscription Subscribe(string subject, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler);

        /// <summary>
        /// Subscribe will create an Asynchronous Subscriber with
        /// interest in a given subject, assign the handler, and immediately
        /// start receiving messages.  The subscriber will use default 
        /// subscriber options.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <param name="qgroup">Name of the queue group.</param>
        /// <param name="handler">A message handler to process messages.</param>
        /// <returns>A new subscription.</returns>
        IStanSubscription Subscribe(string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler);

        /// <summary>
        /// Subscribe will create an Asynchronous Subscriber with
        /// interest in a given subject, assign the handler, and immediately
        /// start receiving messages.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <param name="qgroup">Name of the queue group.</param>
        /// <param name="options">SubscriptionOptions used to create the subscriber.</param>
        /// <param name="handler">A message handler to process messages.</param>
        /// <returns>A new subscription.</returns>
        IStanSubscription Subscribe(string subject, string qgroup, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler);

        /// <summary>
        /// Closes the connection to to the NATS streaming server.  If a
        /// NATS connection was provided, it will remain open.
        /// </summary>
        /// <exception cref="StanCloseRequestException">Error closing the connection.</exception>
        void Close();

        /// <summary>
        /// The NATConnection property gets the underlying NATS connection.
        /// Use this with care. For example, closing the underlying 
        /// NATS conn will invalidate the NATS Streaming connection.
        /// </summary>
        NATS.Client.IConnection NATSConnection { get; }
    }
}
