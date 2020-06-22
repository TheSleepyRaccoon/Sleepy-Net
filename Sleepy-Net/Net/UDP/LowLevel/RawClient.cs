using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using static Sleepy.Net.NetowrkingVars;

namespace Sleepy.Net.UDP
{
    public class RawClient
    {
        readonly Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public readonly IPEndPoint serverEndPoint;

        readonly SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
        readonly ManualResetEvent sendPending = new ManualResetEvent(false);
        Thread SendThread;

        bool serverOffline;
        Thread internalThread;
        DateTime ConnectStartTime;
        DateTime lastSeen = DateTime.Now;
        public bool AutoReconnectOnDisconnect = true;

        public delegate void ConnectionMessage();
        public delegate void PacketMessage(byte[] data, int len);
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;
        public PacketMessage OnPacket;

        public enum State { Terminated, ReadyToConnect, Connecting, Connected, Disconnecting }
        public State state;
        public bool Connected => state == State.Terminated ? false : socket.Connected;

#if SLEEPY_STATS
        public Stats stats = new Stats();
#endif

        // =============== Setup ==================

        public RawClient(string IP, ushort Port)
        {
            serverEndPoint = new IPEndPoint(IPAddress.Parse(IP), Port);

            SendThread = new Thread(SendLoop);
            SendThread.IsBackground = true;
            SendThread.Start();

            state = State.ReadyToConnect;
        }

        ~RawClient()
        {
            if (state != State.Terminated) Close();
        }

        // ============== Connection =================

        public void Connect()
        {
            if (state != State.ReadyToConnect) return;

            socket.Connect(serverEndPoint);

            ConnectStartTime = DateTime.Now;
            Send(SignatureMessage.connectSigMes);
            state = State.Connecting;

            new Thread(RecvLoop) { IsBackground = true }.Start();
            new Thread(RecvLoop) { IsBackground = true }.Start();

            internalThread = new Thread(InternalUpdate);
            internalThread.IsBackground = true;
            internalThread.Start();
        }

        public void Disconnect()
        {
            if (state != State.Connected) return;

            Send(SignatureMessage.disconnectSigMes);
        }

        public void ForceDisconnect(bool notifyServer = true)
        {
            if (state != State.Connected && state != State.Connecting) return;
            state = State.Disconnecting;

            if (notifyServer) Send(SignatureMessage.disconnectSigMes);
            OnDisconnect?.Invoke();

#if SLEEPY_STATS
            Log.WriteNow(string.Format("Client | Total Sent: {0}[{2}] | Total Recieved: {1}[{3}]", Stats.BytesToString(stats.SentBytesTotal), Stats.BytesToString(stats.RecvBytesTotal), stats.RecvTotal, stats.SentTotal));
#endif

            CleanupThread();
            state = State.ReadyToConnect;
        }

        public void Close()
        {
            // NOTE: This func perma shuts down this connection. Client will need to be remade if this func is called. 
            ForceDisconnect(false);
            state = State.Terminated;
            try
            {
                socket.Close(1);
            }
            finally
            {
                CleanupThread();
            }
        }

        // ============= Internal Threads =================

        void InternalUpdate()
        {
            try
            {
                Log.WriteNow("Starting main thread");
                while (state == State.Connecting || state == State.Connected)
                {
                    if (lastSeen.AddSeconds(MaxTimeout) < DateTime.Now)
                    {
                        Log.WriteNow($"Haven't Seen The Server In Longer Than Timeout [{MaxTimeout}s] - Disconnecting");
                        ForceDisconnect(true);
                        return;
                    }

                    if ((state == State.Connecting && ConnectStartTime.AddSeconds(ConnectAttemptTimeout) < DateTime.Now) || state == State.ReadyToConnect)
                    {
                        // TODO: if the server forces us to disconnect, and doesnt want us to reconnect? (do we keep trying on client or reject on server?) 
                        state = State.Connecting;
                        Send(SignatureMessage.connectSigMes);
                        ConnectStartTime = DateTime.Now;
                        if (serverOffline)
                        {
                            serverOffline = false;
                            new Thread(RecvLoop) { IsBackground = true }.Start();
                            new Thread(RecvLoop) { IsBackground = true }.Start();
                        }
                    }

                    Thread.Sleep(1000);
#if SLEEPY_STATS
                    stats.Flush();
#endif
                }
            }
            catch(Exception e)
            {
                Log.WriteNow(e.Message);
            }

        }

        void CleanupThread()
        {
            if (internalThread != null && internalThread.ThreadState == System.Threading.ThreadState.Running)
            {
                internalThread.Abort();
                internalThread = null;
            }
        }

        // =============== Send ==================

        void SendLoop()
        {
            try
            {
                while (state != State.Terminated)
                {
                    sendPending.Reset();

                    while (sendQueue.TryDequeue(out byte[] message))
                    {
                        socket.Send(message);
#if SLEEPY_STATS
                        stats.SentBytesTotal += (ulong)message.Length;
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
                Log.WriteNow("SendLoop Exception On Client, reason: " + exception);
            }
        }

        public void Send(byte[] data)
        {
            if (state == State.Terminated) return;

            sendQueue.Enqueue(data);
            sendPending.Set();
        }

        // ============== Processing Data ===============

        void RecvLoop()
        {
            try
            {
                byte[] buffer = new byte[MaxBufferSize];

                while (state != State.Terminated)
                {
                    int len = socket.Receive(buffer);

                    lastSeen = DateTime.Now;

#if ZT_DEBUG
                    //Log.WriteNow($"RECV: {serverEndPoint}: {len} Bytes");
#endif
#if SLEEPY_STATS
                    stats.RecvBytesTotal += (ulong)len;
                    stats.RecvTotal++;
#endif

                    if (len == SignatureMessage.connMesLen)
                    {
                        SignatureMessage sigMes = SignatureMessage.Deserialize(buffer);
                        if (sigMes.IsValid)
                        {
                            if (sigMes.Connect && state != State.Connected)
                            {
                                state = State.Connected;
                                OnConnect?.Invoke();
                            }
                            else ForceDisconnect(false);
                            continue;
                        }
                    }
#if ZT_DEBUG
                    try { OnPacket(buffer, len); } catch(Exception e) { Log.WriteNow("Client | Failure in OnPacket: " + e.Message); }
#else
                    OnPacket(buffer, len);
#endif
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (SocketException se) 
            {
                if (se.ErrorCode == 10054)
                {
                    // Forcefull reset by host [EG: Server did not respond/Not Running/No Connection]
                    serverOffline = true;
                }
                else if (se.ErrorCode == 10004) { } // WSACancelBlockingCall - Happens on shutdown;
                else Log.WriteNow("RecvLoop Socket Exception On Client, reason: " + se.ErrorCode + " - " + se.Message);
            }
            catch (Exception exception)
            {
                Log.WriteNow("RecvLoop Exception On Client, reason: " + exception);
            }
        }
    }
}
