using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using static Sleepy.Net.NetworkingVars;
using Sleepy.Collections;

namespace Sleepy.Net.UDP
{
    public class RawClient
    {
        static readonly Log Log = new Log("RawClient", "white", true);

        readonly Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public readonly IPEndPoint serverEndPoint;

        readonly SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
        readonly ManualResetEvent sendPending = new ManualResetEvent(false);
        readonly Thread SendThread;

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

            new Thread(RecvLoop) { IsBackground = true }.Start();
            new Thread(RecvLoop) { IsBackground = true }.Start();

            Send(SignatureMessage.connectSigMes);

            state = State.Connecting;

            internalThread = new Thread(InternalUpdate);
            internalThread.IsBackground = true;
            internalThread.Start();            

            ConnectStartTime = DateTime.Now;
        }

        public void Disconnect()
        {
            if (state != State.Connected) return;

            Send(SignatureMessage.disconnectSigMes);
        }

        public void ForceDisconnect(bool notifyServer = true)
        {
            if (state != State.Connected) return;
            state = State.Disconnecting;

            if (notifyServer) Send(SignatureMessage.disconnectSigMes);
            OnDisconnect?.Invoke();

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
#if SLEEPY_STATS
                Log.Write(stats.ToString());
#endif
            }
        }

        // ============= Internal Threads =================

        void InternalUpdate()
        {
            while (state == State.Connecting || state == State.Connected)
            {
                if (lastSeen.AddSeconds(MaxTimeout) < DateTime.Now)
                {
                    Log.Write($"Haven't Seen The Server In Longer Than Timeout [{MaxTimeout}s] - Disconnecting");
                    ForceDisconnect(true);
                }

                if ((state == State.Connecting && ConnectStartTime.AddSeconds(ConnectAttemptTimeout) < DateTime.Now) || state == State.ReadyToConnect)
                {
                    // TODO: if the server forces us to disconnect, and doesnt want us to reconnect? (do we keep trying on client or reject on server?) 
                    state = State.Connecting;
                    Send(SignatureMessage.connectSigMes);
                    ConnectStartTime = DateTime.Now;
                    return;
                }

                Thread.Sleep(1000);
#if SLEEPY_STATS
                stats.Flush();
#endif
            }
        }

        void CleanupThread()
        {
            if (internalThread != null && internalThread.ThreadState == System.Threading.ThreadState.Running)
            {
                if (!internalThread.Join(1500)) Log.Write("Failed to stop thread 'internalThread'");
                internalThread = null;
            }
            Log.Write("Threads Stopped");
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
                        //Log.Write($"SEND: {message.Length}");
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
                Log.Write("SendLoop Exception On Client, reason: " + exception);
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
                EndPoint from = serverEndPoint;

                while (state != State.Terminated)
                {
                    int len = socket.ReceiveFrom(buffer, ref from);
                    lastSeen = DateTime.Now;

#if SLEEPY_STATS
                    //Log.Write($"RECV: {len}");
                    stats.RecvBytesTotal += (ulong)len;
                    stats.RecvTotal++;
#endif

                    if (len == SignatureMessage.connMesLen)
                    {
                        SignatureMessage sigMes = SignatureMessage.Deserialize(buffer);
                        if (sigMes.IsValid)
                        {
                            if (sigMes.Connect)
                            {
                                state = State.Connected;
                                OnConnect?.Invoke();
                            }
                            else ForceDisconnect(false);
                            return;
                        }
                    }

                    OnPacket(buffer, len);
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (SocketException) { /* Noramlly WSA Blcoking Call Canceled when closing */ }
            catch (Exception e)
            {
                Log.Write("RecvLoop Exception On Client, reason: " + e.Message + "\n<b>Stack:</b> " + e.StackTrace);
            }
        }
    }
}
