/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;
using Google.Protobuf;

namespace STAN.Client
{
    // Keep protocol serialization encapulated here.
    internal class ProtocolSerializer
    {
        internal static byte[] marshal(Object req)
        {
            return ((IMessage)req).ToByteArray();
        }

        internal static void unmarshal(byte[] bytes, Object obj)
        {
            ((IMessage)obj).MergeFrom(bytes);
        }

        internal static byte[] createPubMsg(string clientID, string guidValue, string subject, byte[] data)
        {
            PubMsg pm = new PubMsg();

            pm.ClientID = clientID;
            pm.Guid = guidValue;
            pm.Subject = subject;
            if (data != null)
                pm.Data = ByteString.CopyFrom(data);

            return pm.ToByteArray();
        }

        internal static byte[] createAck(MsgProto mp)
        {
            Ack a = new Ack();
            a.Subject = mp.Subject;
            a.Sequence = mp.Sequence;
            return a.ToByteArray();
        }
    }
}
