using System;
using System.Collections.Generic;
using System.Threading;
using Sleepy.Collections;

namespace Sleepy.Net
{
    namespace UDP
    {
        public class Connection
        {
            public int ID;
            public System.Net.EndPoint Conn { get; private set; }

            public Dictionary<ushort, OngoingMessage> OngoingMessages = new Dictionary<ushort, OngoingMessage>();
            public SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
            public ManualResetEvent sendPending = new ManualResetEvent(false);

            public DateTime LastSeen;
            public void UpdateLastSeen() => LastSeen = DateTime.Now;

            public float FTT;

            public Connection(System.Net.EndPoint ep)
            {
                Conn = ep;
                UpdateLastSeen();
            }

            public override int GetHashCode() => Conn.GetHashCode();
            public override bool Equals(object obj) => Conn.GetHashCode() == ((Connection)obj).Conn.GetHashCode();
            public override string ToString() => Conn.ToString();
        }
    }
}