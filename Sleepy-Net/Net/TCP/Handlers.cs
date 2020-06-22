using System;

namespace Sleepy.Net.TCP
{
    public class ServerHandler<T> : Handler where T : IMessage, new()
    {
        public Action<Connection, T> Callback;

        public override void Call(Connection conn, byte[] data, int len)
        {
            T m = MessageUtil.Deserialize<T>(data, len);
            if (m != null) Callback(conn, m);
        }
    }

    public class ServerSyncMessageCall : SyncMessageCall
    {
        public Connection conn;
        public ServerSyncMessageCall(Handler h, byte[] p, int l, Connection conn) : base(h, p, l) { this.conn = conn; }
        public override void Call() { handler.Call(conn, payload, Len); }
    }
}