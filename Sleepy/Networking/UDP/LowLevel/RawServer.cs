#if SLEEPY_SERVER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Sleepy.Collections;
using static Sleepy.Net.NetworkingVars;

namespace Sleepy.Net.UDP
{
    public class RawServer
    {
        static readonly Log Log = new Log("RawServer", "white", true);

        readonly Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public readonly IPEndPoint serverEndPoint;

        struct Packet { public EndPoint conn; public byte[] msg; }
        readonly SafeQueue<Packet> sendQueue = new SafeQueue<Packet>();
        readonly ManualResetEvent sendPending = new ManualResetEvent(false);
        readonly Thread SendThread;

        Thread internalThread;
        public ushort MaxConnections;
        public SafeDictionary<EndPoint, Connection> Connections;

        public delegate void ConnectionMessage(Connection conn);
        public delegate void PacketMessage(Connection conn, byte[] data, int len);
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;
        public PacketMessage OnPacket;

        public enum State { Terminated, RedyToBind, Bound, ShuttingDown }
        public State state;

#if SLEEPY_STATS
        public Stats stats = new Stats();
#endif

        // =============== Setup ==================

        public RawServer(ushort Port, ushort maxConnections = 64)
        {
            MaxConnections = maxConnections;

            serverEndPoint = new IPEndPoint(IPAddress.Any, Port);

            SendThread = new Thread(SendLoop);
            SendThread.IsBackground = true;
            SendThread.Start();

            state = State.RedyToBind;
        }

        ~RawServer()
        {
            if (state != State.Terminated) Stop();
        }

        // ============== Connection =================

        public void Start()
        {
            if (state != State.RedyToBind) return;

            Connections = new SafeDictionary<EndPoint, Connection>(MaxConnections);

            socket.Bind(serverEndPoint);

            for (int i = 0; i < MaxConnections; ++i)
            {
                new Thread(RecvLoop)
                {
                    IsBackground = true
                }.Start();
            }

            state = State.Bound;

            internalThread = new Thread(InternalUpdate);
            internalThread.IsBackground = true;
            internalThread.Start();
        }

        public void Stop()
        {
            if (state != State.Bound) return;

            state = State.ShuttingDown;

            foreach (Connection c in Connections.Values()) Send(c.Conn, SignatureMessage.disconnectSigMes);
            Connections.Clear();

            CleanupThreads();

            state = State.RedyToBind;
        }

        public void Close()
        {
            // NOTE: This func perma shuts down this connection. Client will need to be remade if this func is called. 
            Stop();
            state = State.Terminated;
            try
            {
                socket.Close(1);
            }
            finally
            {
                CleanupThreads();
#if SLEEPY_STATS
                Log.Write(stats.ToString());
#endif
            }
        }

        // ============= Internal Threads =================

        void InternalUpdate()
        {
            List<EndPoint> toRemove = new List<EndPoint>();
            while (state == State.Bound)
            {
                toRemove.Clear();
                foreach (Connection conn in Connections.Values())
                {
                    if (conn.LastSeen.AddSeconds(MaxTimeout) < DateTime.Now)
                    {
                        Send(conn.Conn, SignatureMessage.disconnectSigMes);
                        OnDisconnect?.Invoke(conn);
                        toRemove.Add(conn.Conn);
                    }
                }
                
                for (int i = 0; i < toRemove.Count; ++i)
                {
                    Connections.Remove(toRemove[i]);
                }

                Thread.Sleep(1000);
#if SLEEPY_STATS
                stats.Flush();
#endif
            }
        }

        void CleanupThreads()
        {
            if (internalThread != null && internalThread.ThreadState == ThreadState.Running)
            {
                if (!internalThread.Join(1500)) Log.Write("Failed to stop thread 'internalThread'");
                internalThread = null;
            }
            Log.Write("Threads Stopped");
        }

        // ============== Send ==================

        void SendLoop()
        {
            try
            {
                while (state != State.Terminated)
                {
                    sendPending.Reset();

                    while (sendQueue.TryDequeue(out Packet message))
                    {
                        socket.SendTo(message.msg, message.conn);
#if SLEEPY_STATS
                        //Log.Write($"SEND: {message.msg.Length}");
                        stats.SentBytesTotal += (ulong)message.msg.Length;
                        stats.SentTotal++;
#endif
                    }

                    sendPending.WaitOne();
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception exception)
            {
                Log.Write("SendLoop Exception On Server, reason: " + exception);
            }
        }

        public void Send(EndPoint client, byte[] data)
        {
            if (state == State.Terminated) return;

            sendQueue.Enqueue(new Packet() { conn = client, msg = data });
            sendPending.Set();
        }

        // =============== Process Data ===================

        void RecvLoop()
        {
            try
            {
                byte[] buffer = new byte[MaxBufferSize];
                EndPoint from = serverEndPoint;
                while (state != State.Terminated)
                {
                    int len = socket.ReceiveFrom(buffer, ref from);

                    Connections.TryGetValue(from, out Connection conn);

#if SLEEPY_STATS
                    //Log.Write($"RECV: {len}");
                    stats.RecvBytesTotal += (ulong)len;
                    stats.RecvTotal++;
#endif

                    conn?.UpdateLastSeen();

                    if (len == SignatureMessage.connMesLen)
                    {
                        SignatureMessage sigMes = SignatureMessage.Deserialize(buffer);
                        if (sigMes.IsValid)
                        {
                            if (sigMes.Connect) // client Asking to connect
                            {
                                if (conn == null)
                                {
                                    conn = new Connection(from);
                                    Connections.Add(from, conn);
                                    Send(conn.Conn, SignatureMessage.connectSigMes);
                                    OnConnect?.Invoke(conn);
                                }
                            }
                            else // client asking to disconnect
                            {
                                if (conn != null)
                                {
                                    Connections.Remove(from);
                                    Send(conn.Conn, SignatureMessage.disconnectSigMes);
                                    OnDisconnect?.Invoke(conn);
                                }
                            }
                            continue;
                        }
                    }

                    if (conn != null) OnPacket(conn, buffer, len);
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (SocketException) { /* Noramlly WSA Blcoking Call Canceled when closing */ }
            catch (Exception e)
            {
                Log.Write("RecvLoop Exception On Server, reason: " + e.Message + "\n<b>Stack:</b> " + e.StackTrace);
            }
        }
    }
}
#endif