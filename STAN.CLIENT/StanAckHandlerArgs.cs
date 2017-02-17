/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;

namespace STAN.Client
{
    /// <summary>
    /// This class is used with an acknowleggement event handler indicating 
    /// a message has been acknoweledged by the STAN server.
    /// </summary>
    public class StanAckHandlerArgs : EventArgs
    {
        private string guid;
        private string error;

        private StanAckHandlerArgs() { }

        internal StanAckHandlerArgs(string guid, string error)
        {
            this.guid = guid;
            this.error = error;
        }

        /// <summary>
        /// Contains the GUID of the acknowledged message.
        /// </summary>
        public string GUID
        {
            get
            {
                return guid;
            }
        }

        /// <summary>
        /// Returns an error if applicable.
        /// </summary>
        public string Error
        {
            get
            {
                return this.error;
            }
        }
    }
}
