using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using static Sleepy.Net.NetworkingVars;

namespace Sleepy.Net.TCP
{
    public class RawServer
    {
        public TcpListener listener;
        Thread listenerThread;

        int counter;
        public int NextConnectionID => Interlocked.Increment(ref counter);
        public readonly ConcurrentDictionary<int, Connection> Connections = new ConcurrentDictionary<int, Connection>();

        public bool Active => listenerThread != null && listenerThread.IsAlive;

        [ThreadStatic] static byte[] header;
        [ThreadStatic] static byte[] payload;

        ushort Port;
        ushort MaxConnections;

        public delegate void ConnectionMessage(Connection conn);
        public delegate void PacketMessage(Connection conn, byte[] data, int len);
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;
        public PacketMessage OnPacket;

#if SLEEPY_STATS
        public Stats stats = new Stats();
#endif

        // ============== Setup ================

        public RawServer(ushort port, ushort maxConnections = 64)
        {
            Port = port;
            MaxConnections = maxConnections;
        }

        public bool Start()
        {
            if (Active) return false;

            listenerThread = new Thread(() => { Listen(); })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            listenerThread.Start();
#if SLEEPY_STATS
            System.Timers.Timer statsFlush = new System.Timers.Timer(1000);
            statsFlush.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) =>
            {
                stats.Flush();
            };
            statsFlush.AutoReset = true;
            statsFlush.Enabled = true;
#endif
            return true;
        }

        public void Stop()
        {
            if (!Active) return;

            Log.WriteNow("Server: stopping...");

            listener?.Stop();
            listenerThread?.Interrupt();
            listenerThread = null;

            foreach (KeyValuePair<int, Connection> kvp in Connections)
            {
                TcpClient client = kvp.Value.client;
                try { client.GetStream().Close(); } catch {}
                client.Close();
            }

            Connections.Clear();
            counter = 0;
        }

        public bool Disconnect(int connectionId)
        {
            if (Connections.TryGetValue(connectionId, out Connection token))
            {
                token.client.Close();
                Log.WriteNow("Server.Disconnect connectionId:" + connectionId);
                return true;
            }
            return false;
        }

        void Listen()
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // start listener on all IPv4 and IPv6 address via .Create
                listener = TcpListener.Create(Port);
                listener.Server.NoDelay = NoDelay;
                listener.Server.SendTimeout = SendTimeout;
                listener.Start();

                // keep accepting new clients
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    client.NoDelay = NoDelay;
                    client.SendTimeout = SendTimeout;

                    int connectionID = NextConnectionID;

                    Connection conn = new Connection(connectionID, client);
                    Connections[connectionID] = conn;

                    Thread sendThread = new Thread(() =>
                    {
                        try
                        {
                            SendLoop(conn);
                        }
                        catch (ThreadAbortException) { }
                        catch (Exception exception)
                        {
                            Log.WriteNow("Server send thread exception: " + exception);
                        }
                    });
                    sendThread.IsBackground = true;
                    sendThread.Start();

                    Thread receiveThread = new Thread(() =>
                    {
                        try
                        {
                            ReceiveLoop(conn/*, receiveQueue*/, this);

                            Connections.TryRemove(connectionID, out Connection _);

                            sendThread.Interrupt();
                        }
                        catch (Exception exception)
                        {
                            Log.WriteNow("Server client thread exception: " + exception);
                        }
                    });
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }
            }
            catch (ThreadAbortException) { /* Unity forces this on play */ }
            catch (SocketException) { /* stop server will cause this */ }
            catch (Exception exception)
            {
                Log.WriteNow("Server Exception: " + exception);
            }
        }

        // ============= Send ==============

        static void SendLoop(Connection connection)
        {
            NetworkStream stream = connection.client.GetStream();

            try
            {
                while (connection.client.Connected)
                {
                    connection.sendPending.Reset();

                    if (connection.sendQueue.TryDequeueAll(out byte[][] messages))
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
                        }
                        catch (Exception exception)
                        {
                            Log.WriteNow("Send: stream.Write exception: " + exception);
                            break;
                        }
                    }

                    connection.sendPending.WaitOne();
                }
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception exception)
            {
                Log.WriteNow("SendLoop Exception: connectionId=" + connection.ID + " reason: " + exception);
            }
            finally
            {
                stream.Close();
                connection.client.Close();
            }
        }

        public bool Send(Connection conn, byte[] data)
        {
            if (data.Length > MaxPacketSize)
            {
                Log.WriteNow("Client.Send: message too big: " + data.Length + ". Limit: " + MaxPacketSize);
                return false;
            }

            conn.sendQueue.Enqueue(data);
            conn.sendPending.Set();

#if SLEEPY_STATS
            stats.SentBytesTotal += (ulong)data.Length;
            stats.SentTotal++;
#endif
            return true;
        }
        
        // ============= Recv ==============

        static void ReceiveLoop(Connection connection, RawServer server)
        {
            NetworkStream stream = connection.client.GetStream();
            DateTime messageQueueLastWarning = DateTime.Now;

            try
            {
                server.OnConnect?.Invoke(connection);

                while (true)
                {
                    if (header == null) header = new byte[4];

                    if (!stream.ReadExactly(header, 4)) break;
                    int size = Utilities.BytesToIntBigEndian(header);

                    if (size > MaxPacketSize)
                    {
                        Log.WriteNow("ReadMessageBlocking: possible allocation attack with a header of: " + size + " bytes.");
                        break;
                    }

                    byte[] data = new byte[size];
                    if (!stream.ReadExactly(data, size)) break;

#if SLEEPY_STATS
                    server.stats.RecvBytesTotal += (ulong)data.Length;
                    server.stats.RecvTotal++;
#endif
                    server.OnPacket(connection, data, data.Length);
                    //                    try
                    //                    {
                    //                        server.OnPacket(connection, data, data.Length);
                    //                    }
                    //#if SLEEPY_DEBUG
                    //                    catch (Exception e)
                    //                    {
                    //                        Log.WriteNow($"Error: On Packet. Likely a callback had an error in it. -> {e.Message}\nStack: {e.StackTrace}");
                    //#else
                    //                    catch
                    //                    {
                    //#endif
                    //                    }
                }
            }
            catch (Exception exception)
            {
                Log.WriteNow("ReceiveLoop: finished receive function for connectionId=" + connection.ID + " reason: " + exception);
            }
            finally
            {
                stream.Close();
                connection.client.Close();

                server.OnDisconnect?.Invoke(connection);
            }
        }
    }
}
