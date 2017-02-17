/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/

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
