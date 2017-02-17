/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using NATS.Client;

namespace STAN.Client
{
    /// <summary>
    /// NATS Streaming client constants.
    /// </summary>
    public class StanConsts
    {
        /// <summary>
        /// NATS C# streaming client version.
        /// </summary>
	    static public readonly string Version = "0.0.1";

        /// <summary>
        /// DefaultNatsURL is the default URL the client connects to.
        /// </summary>
        static public readonly string DefaultNatsURL = "nats://localhost:4222";

        /// <summary>
        /// DefaultConnectWait is the default timeout used for the connect operation.
        /// </summary>
        static public readonly int    DefaultConnectWait = 2000;

        /// <summary>
        /// DefaultDiscoverPrefix is the prefix subject used to connect to the NATS Streaming server.
        /// </summary>
        static public readonly string DefaultDiscoverPrefix = "_STAN.discover";

        /// <summary>
        /// DefaultACKPrefix is the prefix subject used to send ACKs to the NATS Streaming server.
        /// </summary>
        static public readonly string DefaultACKPrefix = "_STAN.acks";

        /// <summary>
        /// DefaultMaxPubAcksInflight is the default maximum number of published messages
	    /// without outstanding ACKs from the server.
        /// </summary>
        static public readonly long DefaultMaxPubAcksInflight = 16384;

        /// <summary>
        /// DefaultAckWait indicates how long the server should wait for an ACK before resending a message.
        /// </summary>
        static public readonly long DefaultAckWait = 30000;

        /// <summary>
        /// DefaultMaxInflight indicates how many messages with outstanding ACKs the server can send.
        /// </summary>
        static public readonly int  DefaultMaxInflight = 1024;
   }
}
