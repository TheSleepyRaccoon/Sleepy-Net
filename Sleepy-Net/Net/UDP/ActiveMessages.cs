using System;
using System.Collections.Generic;

namespace Sleepy.Net.UDP
{
    public class MessagePartsActive
    {
        public ushort ID;
        public List<byte[]> Parts;
        public bool[] partsConfirmed;

        public int MaxPacketsOut = 10;
        public int PacketsIn = 0;
        public float WaitTime = 0.25f;

        protected DateTime LastSendMessagePart = DateTime.MinValue;

        public MessagePartsActive(int NumOfParts) => partsConfirmed = new bool[NumOfParts];

        public virtual bool Process() => true;
        public virtual void AddPart(byte[] part, bool sendImmediately = true) => Parts.Add(part);
        public virtual void RecievedPart(int partNum) { partsConfirmed[partNum] = true; ++PacketsIn; }

        public float Progress
        {
            get
            {
                int totalDone = 0;
                for (int i = 0; i < partsConfirmed.Length; ++i) totalDone += partsConfirmed[i] ? 1 : 0;
                return (float)totalDone / partsConfirmed.Length;
            }
        }
    }

    public class ClientMessagePartsActive : MessagePartsActive
    {
        public Client _Driver;

        public ClientMessagePartsActive(int NumOfParts, ushort id, UDP.Client driver) : base(NumOfParts)
        {
            ID = id;
            _Driver = driver;
            Parts = new List<byte[]>();
        }

        public override bool Process()
        {
            if (LastSendMessagePart + TimeSpan.FromSeconds(WaitTime) <= DateTime.UtcNow ||
                PacketsIn >= MaxPacketsOut / 2)
            {
                PacketsIn = 0;

                int numSent = 0;
                for (int i = 0; i < partsConfirmed.Length && numSent < MaxPacketsOut; ++i)
                {
                    if (!partsConfirmed[i])
                    {
                        byte[] d = Parts[i];
                        _Driver.InternalSend(d);
                        numSent++;
                    }
                }
                if (numSent == 0) return true;

                LastSendMessagePart = DateTime.UtcNow;
            }

            return false;
        }
    }

#if SLEEPY_SERVER
    public class ServerMessagePartsActive : MessagePartsActive
    {
        public Server _Driver;
        public Connection conn;

        public ServerMessagePartsActive(int NumOfParts, ushort id, Connection c, Server driver) : base(NumOfParts)
        {
            ID = id;
            conn = c;
            _Driver = driver;
            Parts = new List<byte[]>();
        }

        public override bool Process()
        {
            if (LastSendMessagePart + TimeSpan.FromSeconds(WaitTime) <= DateTime.UtcNow ||
                PacketsIn >= MaxPacketsOut / 2)
            {
                PacketsIn = 0;

                int numSent = 0;
                for (int i = 0; i < partsConfirmed.Length && numSent < MaxPacketsOut; ++i)
                {
                    if (!partsConfirmed[i])
                    {
                        byte[] d = Parts[i];
                        _Driver.InternalSend(conn.Conn, d);
                        numSent++;
                    }
                }
                if (numSent == 0) return true;

                LastSendMessagePart = DateTime.UtcNow;
            }

            return false;
        }
    }
#endif
}