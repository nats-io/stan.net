/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;

namespace STAN.Client
{
    /// <summary>
    /// Arguments passed to the StanMsgHandler.
    /// </summary>
    public class StanMsgHandlerArgs : EventArgs
    {
        StanMsg msg = null;

        internal StanMsgHandlerArgs(StanMsg m)
        {
            msg = m;
        }

        /// <summary>
        /// The received message.
        /// </summary>
        public StanMsg Message
        {
            get
            {
                return msg;
            }
        }
    }
}
