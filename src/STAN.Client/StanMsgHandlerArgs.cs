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
    /// Arguments passed to the StanMsgHandler.
    /// </summary>
    public class StanMsgHandlerArgs : EventArgs
    {
        StanMsg msg = null;

        /// <summary>
        ///  Constructor for generating a StanMsgHandlerArgs object.  Used for application unit testing.
        /// </summary>
        /// <remarks>
        /// Objects of this type are normally generated internally by the NATS streaming client.
        /// This constructor has been provided to facilitate application unit testing.
        /// </remarks>
        /// <param name="data">The message payload.</param>
        /// <param name="redelivered">True if the message may have been redelivered.</param>
        /// <param name="subject">Subject of the message.</param>
        /// <param name="timestamp">Message timestamp, nanoseconds since epoch.(1/1/1970)</param>
        /// <param name="sequence">Sequence number of the message.</param>
        /// <param name="subscription">Subscription of the message.  Must be a valid streaming subscription or null.</param>
        public StanMsgHandlerArgs(byte[] data, bool redelivered, string subject, long timestamp, ulong sequence, IStanSubscription subscription)
        {
            msg = new StanMsg(data, redelivered, subject, timestamp, sequence, subscription);
        }

        /// <summary>
        /// Constructor for generating a StanMsgHandlerArgs object.  Used for application unit testing. 
        /// </summary>
        /// <param name="m">A NATS streaming message.</param>
        public StanMsgHandlerArgs(StanMsg m)
        {
            msg = m;
        }

        /// <summary>
        /// The received message.
        /// </summary>
        public StanMsg Message
        {
            get
            {
                return msg;
            }
        }
    }
}
