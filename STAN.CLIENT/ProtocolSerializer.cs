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
using Google.Protobuf;

namespace STAN.CLIENT
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
