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
	    static public readonly string Version = "0.1.5";

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

        /// <summary>
        /// DefaultPingInterval is the default interval (in milliseconds) at which a connection sends a PING to the server.
        /// </summary>
        static public readonly int DefaultPingInterval = 5000;

        /// <summary>
        /// DefaultPingMaxOut is the number of PINGs without a response before the connection is considered lost.
        /// </summary>
        static public readonly int DefaultPingMaxOut = 3;

        // Clients send connID in ConnectRequest and PubMsg, and the server
        // listens and responds to client PINGs. The validity of the
        // connection (based on connID) is checked on incoming PINGs.
        static internal readonly int protocolOne = 1;
    }
}
