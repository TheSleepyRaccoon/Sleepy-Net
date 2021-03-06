﻿using System;

namespace Sleepy.Net
{
    // TODO: Optimize these
    public class Handler
    {
        public ushort channel;
        public ushort MessageID;

        public virtual void Call(byte[] data, int len) { }
        public virtual void Call(UDP.Connection conn, byte[] data, int len) { }
        public virtual void Call(TCP.Connection conn, byte[] data, int len) { }
    }

    public class Handler<T> : Handler where T : IMessage, new()
    {
        public Action<T> Callback;

        public override void Call(byte[] data, int len)
        {
            T m = MessageUtil.Deserialize<T>(data, len);
            if (m != null) Callback(m);
        }
    }

    public class SyncMessageCall
    {
        public Handler handler;
        public byte[] payload;
        public int Len;
        public SyncMessageCall(Handler h, byte[] p, int l) { handler = h; payload = p; Len = l; }
        public virtual void Call() { try { handler.Call(payload, Len); } catch { } }
    }

    public class ClientSyncMessageCall : SyncMessageCall
    {
        public ClientSyncMessageCall(Handler h, byte[] p, int l) : base(h, p, l) { }
        public override void Call() { try { handler.Call(payload, Len); } catch { } }
    }
}