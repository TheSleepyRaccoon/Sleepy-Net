using System;
using System.Net.Sockets;
using System.Threading;
using static Sleepy.Net.NetowrkingVars;

namespace Sleepy.Net.TCP
{
    public class RawClient
    {
        public string IP;
        public ushort Port;
        public TcpClient client;
        Thread receiveThread;
        Thread sendThread;

        volatile bool _Connecting;
        public bool Connecting => _Connecting;
        public bool Connected => client != null && client.Client != null && client.Client.Connected;

        readonly SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
        readonly ManualResetEvent sendPending = new ManualResetEvent(false);

        [ThreadStatic] static byte[] header;
        [ThreadStatic] static byte[] payload;

        public delegate void ConnectionMessage();
        public delegate void PacketMessage(byte[] data, int len);
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;
        public PacketMessage OnPacket;

#if SLEEPY_STATS
        public Stats stats = new Stats(); // TODO: Flush this
#endif

        // =============== Setup ==================

        public RawClient(string ip, ushort port)
        {
            IP = ip;
            Port = port;
        }

        public void Connect()
        {
            if (Connecting || Connected) return;
            _Connecting = true;

            client = new TcpClient
            {
                Client = null
            };

            sendQueue.Clear();

            receiveThread = new Thread(() => { InternalConnect(); });
            receiveThread.IsBackground = true;
            receiveThread.Start();

#if SLEEPY_STATS
            System.Timers.Timer statsFlush = new System.Timers.Timer(1000);
            statsFlush.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) =>
            {
                stats.Flush();
            };
            statsFlush.AutoReset = true;
            statsFlush.Enabled = true;
#endif
        }

        public void Disconnect()
        {
            if (!Connecting && !Connected) return;

            client.Close();

            receiveThread?.Interrupt();

            _Connecting = false;

            sendQueue.Clear();

            client = null;
        }

        void InternalConnect()
        {
            try
            {
                client.Connect(IP, Port);
                _Connecting = false;

                client.NoDelay = NoDelay;
                client.SendTimeout = SendTimeout;

                sendThread = new Thread(() => { SendLoop(this); });
                sendThread.IsBackground = true;
                sendThread.Start();

                ReceiveLoop(this);
            }
            catch (SocketException exception)
            {
                Log.WriteNow("Client Recv: failed to connect to ip=" + IP + " port=" + Port + " reason=" + exception);
                OnDisconnect?.Invoke();
            }
            catch (ThreadInterruptedException) { }
            catch (ThreadAbortException) { }
            catch (Exception exception)
            {
                Log.WriteNow("Client Recv Exception: " + exception);
            }

            sendThread?.Interrupt();

            _Connecting = false;

            client?.Close();
        }

        // ================= Send ===================

        static void SendLoop(RawClient client)
        {
            NetworkStream stream = client.client.GetStream();

            try
            {
                while (client.Connected)
                {
                    client.sendPending.Reset();

                    if (client.sendQueue.TryDequeueAll(out byte[][] messages))
                    {
                        if (!InternalSend(stream, messages)) break;
                    }

                    client.sendPending.WaitOne();
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception exception)
            {
                Log.WriteNow("SendLoop Exception On Client, reason: " + exception);
            }
            finally
            {
                stream.Close();
                client.client?.Close();
            }
        }

        static bool InternalSend(NetworkStream stream, byte[][] messages)
        {
            try
            {
                if (header == null) header = new byte[4];
                if (payload == null) payload = new byte[MaxBufferSize];

                int position = 0;
                for (int i = 0; i < messages.Length; ++i)
                {
                    Utilities.IntToBytesBigEndianNonAlloc(messages[i].Length, header);

                    Array.Copy(header, 0, payload, position, header.Length);
                    Array.Copy(messages[i], 0, payload, position + header.Length, messages[i].Length);
                    position += header.Length + messages[i].Length;
                }

                stream.Write(payload, 0, position);

                return true;
            }
            catch (Exception exception)
            {
                Log.WriteNow("Send: stream.Write exception: " + exception);
                return false;
            }
        }

        public bool Send(byte[] data)
        {
            if (!Connected || data.Length > MaxPacketSize) return false;

            sendQueue.Enqueue(data);
            sendPending.Set();
#if SLEEPY_STATS
            stats.SentBytesTotal += (ulong)data.Length;
            stats.SentTotal++;
#endif
            return true;
        }

        // =============== Recv ===================

        static void ReceiveLoop(RawClient client)
        {
            NetworkStream stream = client.client.GetStream();
            DateTime messageQueueLastWarning = DateTime.Now;

            try
            {
                client.OnConnect?.Invoke();

                while (true)
                {
                    if (!InternalRecv(stream, out byte[] data)) break;

#if SLEEPY_STATS
                    client.stats.RecvBytesTotal += (ulong)data.Length;
                    client.stats.RecvTotal++;
#endif
                    client.OnPacket(data, data.Length);
                }
            }
            catch (Exception exception)
            {
                Log.WriteNow("ReceiveLoop: finished receive function for Client, reason: " + exception);
            }
            finally
            {
                stream.Close();
                client.client?.Close();

                client.OnDisconnect?.Invoke();
            }
        }

        static bool InternalRecv(NetworkStream stream, out byte[] content)
        {
            content = null;

            if (header == null) header = new byte[4];

            if (!stream.ReadExactly(header, 4)) return false;
            int size = Utilities.BytesToIntBigEndian(header);

            if (size > MaxPacketSize)
            {
                Log.WriteNow("ReadMessageBlocking: possible allocation attack with a header of: " + size + " bytes.");
                return false;
            }

            content = new byte[size];
            return stream.ReadExactly(content, size);
        }
    }
}
