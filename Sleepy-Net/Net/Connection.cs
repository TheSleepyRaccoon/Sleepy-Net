using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Sleepy.Net
{
    public class SignatureMessage
    {
        public const string Signature = "ZTUDP255";
        public string Sig;
        public bool Connect;
        public bool IsValid { get { return Sig == Signature; } }

        public SignatureMessage(string s, bool c)
        {
            Sig = s;
            Connect = c;
        }

        public byte[] Serialize()
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {
                    writer.Write(Sig);
                    writer.Write(Connect);
                }
                return m.ToArray();
            }
        }

        public static SignatureMessage Deserialize(byte[] data)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    try
                    {
                        return new SignatureMessage(reader.ReadString(), reader.ReadBoolean());
                    }
                    catch
                    {
                        return new SignatureMessage("INVALID", false);
                    }
                }
            }
        }

        // ========================

        public static byte[] connectSigMes = new SignatureMessage(Signature, true).Serialize();
        public static byte[] disconnectSigMes = new SignatureMessage(Signature, false).Serialize();
        public static int connMesLen = new SignatureMessage(Signature, true).Serialize().Length;
    }
}