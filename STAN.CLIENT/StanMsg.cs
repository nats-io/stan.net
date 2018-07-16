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
    /// A StanMsg object is a received NATS streaming message.
    /// </summary>
    public sealed class StanMsg
    {
        AsyncSubscription _sub;

        internal StanMsg(MsgProto proto, AsyncSubscription sub)
        {
            Proto = proto;
            _sub = sub ?? throw new StanBadSubscriptionException();
        }

        internal MsgProto Proto { get; }

        /// <summary>
        /// Gets the subscription this message was received upon.
        /// </summary>
        public IStanSubscription Subscription => _sub;

        /// <summary>
        /// Get the time stamp of the message represeneted as Unix nanotime.
        /// </summary>
        public long Time => Proto.Timestamp;

        /// <summary>
        /// Get the timestamp of the message.
        /// </summary>
        public DateTime TimeStamp => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddTicks(Proto.Timestamp / 100);

        /// <summary>
        /// Acknowledge a message.
        /// </summary>
        public void Ack() => _sub.ManualAck(this);

        /// <summary>
        /// Gets the sequence number of a message.
        /// </summary>
        public ulong Sequence => Proto.Sequence;

        /// <summary>
        /// Gets the subject of the message.
        /// </summary>
        public string Subject => Proto.Subject;

        /// <summary>
        /// Get the data field (payload) of a message.
        /// </summary>
        public byte[] Data => Proto.Data?.ToByteArray();

        /// <summary>
        /// The redelivered property if true if this message has been redelivered, false otherwise.
        /// </summary>
        public bool Redelivered => Proto.Redelivered;
    }
}
