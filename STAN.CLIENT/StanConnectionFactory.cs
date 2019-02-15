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

namespace STAN.CLIENT
{
    /// <summary>
    /// Creates a connection to the NATS streaming server.
    /// </summary>
    public sealed class StanConnectionFactory
    {
        /// <summary>
        /// Creates a connection to the server.
        /// </summary>
        /// <param name="clusterID"></param>
        /// <param name="clientID"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public IStanConnection CreateConnection(string clusterID, string clientID, StanOptions options)
        {
            return new Connection(clusterID, clientID, options);
        }

        /// <summary>
        /// Creates a connection to the server.
        /// </summary>
        /// <param name="clusterID"></param>
        /// <param name="clientID"></param>
        /// <returns></returns>
        public IStanConnection CreateConnection(string clusterID, string clientID)
        {
            return new Connection(clusterID, clientID, null);
        }
    }
}
