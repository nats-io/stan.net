/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
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
        internal long ackTimeout = StanConsts.DefaultConnectWait;
        internal string discoverPrefix = StanConsts.DefaultDiscoverPrefix;
        internal long maxPubAcksInflight = StanConsts.DefaultMaxPubAcksInflight;

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
                natsConn = value;
            }
        }

        /// <summary>
        /// ConnectTimeout is an Option to set the timeout for establishing a connection.
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
        /// for a published message.  The value must be greater than zero.
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
        /// of published  messages without outstanding ACKs from the server.
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
        /// Returns the default connection options.
        /// </summary>
        /// <returns></returns>
        public static StanOptions GetDefaultOptions()
        {
            return new StanOptions();
        }
    }
}
