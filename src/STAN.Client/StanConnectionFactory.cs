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
    /// Creates a connection to the NATS streaming server.
    /// </summary>
    public sealed class StanConnectionFactory
    {
        /// <summary>
        /// Creates a connection to the server.
        /// </summary>
        /// <param name="clusterID">The cluster ID of streaming server.</param>
        /// <param name="clientID">A unique ID for this connection.</param>
        /// <param name="options">Connection options.</param>
        /// <exception cref="StanConnectionException">Thrown when core NATS is unable to connect to a server.</exception>
        /// <exception cref="StanConnectRequestTimeoutException">Thrown when there is an invalid cluster id or other connectivity issues.</exception>
        /// <returns>A connection to a streaming server.</returns>
        public IStanConnection CreateConnection(string clusterID, string clientID, StanOptions options)
        {
            return new Connection(clusterID, clientID, options);
        }

        /// <summary>
        /// Creates a connection to the server.
        /// </summary>
        /// <param name="clusterID">The cluster ID of streaming server.</param>
        /// <param name="clientID">A unique ID for this connection.</param>
        /// <exception cref="StanConnectionException">Thrown when core NATS is unable to connect to a server.</exception>
        /// <exception cref="StanConnectRequestTimeoutException">Thrown when there is an invalid cluster id or other connectivity issues.</exception>
        /// <returns>A connection to a streaming server.</returns>
        public IStanConnection CreateConnection(string clusterID, string clientID)
        {
            return new Connection(clusterID, clientID, null);
        }
    }
}
