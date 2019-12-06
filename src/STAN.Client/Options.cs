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
using NATS.Client;

namespace STAN.Client
{
    /// <summary>
    /// Options available to configure a connection to the NATS streaming server.
    /// </summary>
    public sealed class StanOptions
    {
        internal string natsURL = StanConsts.DefaultNatsURL;
        internal IConnection natsConn = null;
        internal int connectTimeout = StanConsts.DefaultConnectWait;
        internal long ackTimeout = StanConsts.DefaultAckWait;
        internal string discoverPrefix = StanConsts.DefaultDiscoverPrefix;
        internal long maxPubAcksInflight = StanConsts.DefaultMaxPubAcksInflight;
        internal int pingMaxOut = StanConsts.DefaultPingMaxOut;
        internal int pingInterval = StanConsts.DefaultPingInterval;
        internal long pubAckPendingMessageLimit = StanConsts.DefaultPubAckPendingMessageLimit;
        internal long pubAckPendingBytesLimit = StanConsts.DefaultPubAckPendingBytesLimit;

        internal StanOptions() { }

        internal static string DeepCopy(string value)
        {
            if (value == null)
                return null;

            return new string(value.ToCharArray());
        }

        internal StanOptions(StanOptions options)
        {
            ackTimeout = options.ackTimeout;
            NatsURL = DeepCopy(options.NatsURL);
            ConnectTimeout = options.ConnectTimeout;
            PubAckWait = options.PubAckWait;
            DiscoverPrefix = DeepCopy(options.DiscoverPrefix);
            MaxPubAcksInFlight = options.MaxPubAcksInFlight;
            NatsConn = options.natsConn;
            PingInterval = options.PingInterval;
            PingMaxOutstanding = options.PingMaxOutstanding;
            ConnectionLostEventHandler = options.ConnectionLostEventHandler;
            pubAckPendingMessageLimit = options.pubAckPendingMessageLimit;
            pubAckPendingBytesLimit = options.pubAckPendingBytesLimit;
        }

        /// <summary>
        /// Gets or sets the url to connect to a NATS server.
        /// </summary>
	    public string NatsURL
        {
            get 
            {
                return natsURL;
            }
            set
            {
                natsURL = value != null ? value : StanConsts.DefaultNatsURL;
            }
        }

        /// <summary>
        /// Sets the underlying NATS connection to be used by a NATS Streaming 
        /// connection object.
        /// </summary>
        public IConnection NatsConn
        {
            set
            {
                if(value != null && value.Opts.ReconnectBufferSize != Options.ReconnectBufferDisabled)
                    throw new ArgumentException("The underlying NATS connection should have buffers during reconnect disabled. E.g controlled via: 'Opts.ReconnectBufferSize = Options.ReconnectBufferDisabled'", nameof(value));

                natsConn = value;
            }
        }

        /// <summary>
        /// ConnectTimeout is an Option to set the timeout for establishing a connection
        /// in milliseconds.
        /// </summary>
        public int ConnectTimeout
        {
            get
            {
                return connectTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "ConnectTimeout must be greater than zero.");

                connectTimeout = value;
            }
        }

        /// <summary>
        /// PubAckWait is an option to set the timeout for waiting for an ACK 
        /// for a published message in milliseconds.  The value must be greater than zero.
        /// </summary>
        public long PubAckWait
        {
            get
            {
                return ackTimeout;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "PubAckWait must be greater than zero.");

                ackTimeout = value;
            }
        }

        /// <summary>
        /// Sets the discover prefix used in connecting to the NATS streaming server.
        /// This must match the settings on the NATS streaming sever.
        /// </summary>
        public string DiscoverPrefix
        {
            get
            {
                return discoverPrefix;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value", "DiscoverPrefix cannot be null.");

                discoverPrefix = value;
            }
        }

        /// <summary>
        /// MaxPubAcksInflight is an Option to set the maximum number 
        /// of published messages without outstanding ACKs from the server.
        /// </summary>
        public long MaxPubAcksInFlight
        {
            get
            {
                return maxPubAcksInflight;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "MaxPubAcksInFlight must be greater than zero.");

                maxPubAcksInflight = value;
            }
        }

        /// <summary>
        /// PingMaxOutstanding is an option to set the maximum number 
        /// of outstanding pings with the streaming server.
        /// See <see cref="StanConsts.DefaultPingMaxOut"/>.
        /// </summary>
        public int PingMaxOutstanding
        {
            get
            {
                return pingMaxOut;
            }
            set
            {
                if (value <= 2)
                    throw new ArgumentOutOfRangeException("value", value, "PingMaxOutstanding must be greater than two.");

                pingMaxOut = value;
            }
        }

        /// <summary>
        /// PingInterval is an option to set the interval of pings in milliseconds.
        /// See <see cref="StanConsts.DefaultPingInterval"/>.
        /// </summary>
        public int PingInterval
        {
            get
            {
                return pingInterval;
            }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", value, "PingInterval must be greater than zero.");

                pingInterval = value;
            }
        }

        /// <summary>
        /// Gets the PubAck MessageLimit. Use <see cref="SetPubAckPendingLimits"/> to update.
        /// </summary>
        public long PubAckPendingMessageLimit => pubAckPendingMessageLimit;
        
        /// <summary>
        /// Gets the PubAck BytesLimit. Use <see cref="SetPubAckPendingLimits"/> to update.
        /// </summary>
        public long PubAckPendingBytesLimit => pubAckPendingBytesLimit;

        /// <summary>
        /// Sets the limits for pending publish acknowledgement messages and bytes.
        /// </summary>
        /// <remarks>Zero (<c>0</c>) is not allowed. Negative values indicate that the
        /// given metric is not limited.</remarks>
        /// <param name="messageLimit">The maximum number of pending messages.</param>
        /// <param name="bytesLimit">The maximum number of pending bytes of payload.</param>
        public void SetPubAckPendingLimits(long messageLimit, long bytesLimit)
        {
            if(messageLimit == 0)
                throw new ArgumentOutOfRangeException(nameof(messageLimit), "The pending message limit must not be zero.");
            
            if(bytesLimit == 0)
                throw new ArgumentOutOfRangeException(nameof(bytesLimit), "The pending bytes limit must not be zero.");

            pubAckPendingMessageLimit = messageLimit;
            pubAckPendingBytesLimit = bytesLimit;
        }

        /// <summary>
        /// Represents the method that will handle an event raised
        /// when a connection has been disconnected from a streaming server.
        /// </summary>
        public EventHandler<StanConnLostHandlerArgs> ConnectionLostEventHandler { get; set; }

        /// <summary>
        /// Returns the default connection options.
        /// </summary>
        /// <returns></returns>
        public static StanOptions GetDefaultOptions()
        {
            return new StanOptions();
        }
    }
}
