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

namespace STAN.CLIENT
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
