using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Sleepy.Net.TCP
{
    public class Connection
    {
        public int ID;
        public System.Net.EndPoint Conn { get; private set; }

        public TcpClient client;
        public Dictionary<ushort, OngoingMessage> OngoingMessages = new Dictionary<ushort, OngoingMessage>();
        public SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
        public ManualResetEvent sendPending = new ManualResetEvent(false);

        public DateTime LastSeen;
        public void UpdateLastSeen() => LastSeen = DateTime.Now;

        public Connection() { }
        public Connection(int conID, TcpClient c)
        {
            client = c;
            ID = conID;
            Conn = client.Client.RemoteEndPoint;
        }

        public override int GetHashCode() => Conn.GetHashCode();
        public override bool Equals(object obj) => Conn.GetHashCode() == ((Connection)obj).Conn.GetHashCode();
        public override string ToString() => Conn.ToString();
    }
}