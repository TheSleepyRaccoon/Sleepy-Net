using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Sleepy.Security;
using static Sleepy.Net.NetowrkingVars;

namespace Sleepy.Net.TCP
{
    public class Server
    {
        public RawServer rawServer;
        public bool Active => rawServer.Active;

        ConcurrentQueue<ServerSyncMessageCall> SyncMessagesToProcess;

        readonly List<Handler> Handlers;

        // ======== Encypted Message ===============
        RSAEncryption.RSAKeys RSAServerKeys;
        Dictionary<Connection, string> AESKeys;
        // ========= End Encrpted Message ==================

        public delegate void ConnectionMessage(Connection conn);
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;

#if SLEEPY_STATS
        public Stats stats => rawServer.stats;
#endif

        // ============== Setup ================

        public Server(ushort Port, ushort maxConnections = 64)
        {
            rawServer = new RawServer(Port, maxConnections);
            rawServer.OnConnect += InternalOnConnect;
            rawServer.OnDisconnect += InternalOnDisconnect;
            rawServer.OnPacket += ProcessData;

            Handlers = new List<Handler>(100);
        }

        void ResetData()
        {
            SyncMessagesToProcess = new ConcurrentQueue<ServerSyncMessageCall>();
            AESKeys = new Dictionary<Connection, string>();
        }

        // =============== Bindings ===================

        public void Bind<T>(ushort msgType, Action<Connection, T> callback) where T : IMessage, new()
        {
            Handlers.Add(new ServerHandler<T>() { channel = msgType, Callback = callback });
        }

        public void Unbind<T>(short msgType, Action<Connection, T> callback) where T : IMessage, new()
        {
            for (int i = 0; i < Handlers.Count; ++i)
            {
                Handler h = Handlers[i];
                if (h.channel == msgType)
                {
                    ServerHandler<T> converted = h as ServerHandler<T>;
                    if (converted.Callback == callback)
                    {
                        Handlers.Remove(h);
                        return;
                    }
                }
            }
        }

        // ============== Connection =================

        public bool Start()
        {
            if (Active) return false;
            ResetData();

            // ======== Encypted Message ===============
            Log.WriteNow("Server | Setting Up Encryption");
#if ZT_DEBUG
            RSAServerKeys = RSAEncryption.GenerateKeys(1024);
#else
            RSAServerKeys = Sleepy.Security.RSAEncryption.GenerateKeys(4096);
#endif
            Log.WriteNow("Server | Encryption Setup");
            // ========= End Encrpted Message ==================

            rawServer.Start();
            return true;
        }

        public void Stop() => rawServer.Stop();
        public bool Disconnect(int connectionId) => rawServer.Disconnect(connectionId);

        void InternalOnConnect(Connection conn) => OnConnect?.Invoke(conn);        
        void InternalOnDisconnect(Connection conn) => OnDisconnect?.Invoke(conn);

        // ============= Send ==============

        public void Send<T>(Connection connection, T message) where T : IMessage => Send(connection, ref message);
        public void Send<T>(Connection connection, ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;

            byte[] buffer = MessageUtil.Serialize(ref message);
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 6);

            rawServer.Send(connection, buffer);
        }

        public void SendMulti<T>(Connection[] connections, T message) where T : IMessage => SendMulti(connections, ref message);
        public void SendMulti<T>(Connection[] connections, ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;

            byte[] buffer = MessageUtil.Serialize(ref message);
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 6);

            for (int i = 0; i < connections.Length; ++i)
            {
                rawServer.Send(connections[i], buffer);
            }
        }

        public void SendLarge<T>(Connection connection, T message, byte[] Data = null) where T : IMessage => SendLarge(connection, ref message, Data);
        public void SendLarge<T>(Connection connection, ref T message, byte[] Data = null) where T : IMessage
        {
            if (Data == null)
            {
                // Message not already serialized
                Data = MessageUtil.Serialize(ref message);
                BitConverter.GetBytes(Data.Length).CopyTo(Data, 6);
            }

            if (Data.Length < MaxPacketSize)
            {
                rawServer.Send(connection, Data);
                return;
            }

            message.TotalParts = (ushort)((Data.Length / MaxPacketSize) + 1);

            int remaining = Data.Length % MaxPacketSize;
            int amount = message.TotalParts - 1;
            ushort id = message.ID;

            // Issue to full Packet Size Parts
            for (ushort i = 0; i < amount; ++i)
            {
                MessagePart part = new MessagePart(message.Channel, Data.Length, i, message.TotalParts, id)
                {
                    Data = Data.SubArray(i * MaxPacketSize, MaxPacketSize)
                };
                rawServer.Send(connection, MessageUtil.Serialize(ref part));
            }

            // Send the end Part that contains the rest of the data
            MessagePart endPart = new MessagePart(message.Channel, Data.Length, (ushort)amount, (ushort)(amount + 1), id)
            {
                Data = Data.SubArray(amount * MaxPacketSize, remaining)
            };
            rawServer.Send(connection, MessageUtil.Serialize(ref endPart));
        }

        public void EncrptedSend<T>(Connection connection, T message) where T : IMessage => EncrptedSend(connection, ref message);
        public void EncrptedSend<T>(Connection connection, ref T message) where T : IMessage
        {
            if (!AESKeys.TryGetValue(connection, out string Key))
            {
                Log.WriteNow("Failed to find AES Key for this connection. Failed to send encrypted message");
                return;
            }

            AESMessage aesMessage = new AESMessage(message)
            {
                ID = message.ID
            };
            aesMessage.Encrypt(Key);
            byte[] buffer = MessageUtil.Serialize(ref aesMessage);
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 6);

            rawServer.Send(connection, buffer);
        }

        public void EncrptedSendLarge<T>(Connection connection, T message) where T : IMessage => EncrptedSendLarge(connection, ref message);
        public void EncrptedSendLarge<T>(Connection connection, ref T message) where T:IMessage
        {
            if (!AESKeys.TryGetValue(connection, out string Key))
            {
                Log.WriteNow("Failed to find AES Key for this connection. Failed to send encrypted message");
                return;
            }

            AESMessage aesMessage = new AESMessage(message)
            {
                ID = message.ID
            };
            aesMessage.Encrypt(Key);
            byte[] data = MessageUtil.Serialize(ref aesMessage);

            SendLarge(connection, ref aesMessage, data);
        }

        // ============= Recv ==============

        void ProcessData(Connection conn, byte[] payload, int len)
        {
            Header header = MessageUtil.DeserializeHeader(payload, len);

            // ======== Encypted Message ===============
            if (header.Channel == MessageTypes.AESMessage)
            {
                if (AESKeys.TryGetValue(conn, out string key))
                {
                    try
                    {
                        AESMessage message = AESMessage.Desserialize(payload, len);
                        message.Decrypt(key);
                        payload = message.message;
                        header = MessageUtil.DeserializeHeader(payload, payload.Length);

                        if (header.Parted) ProcessMesagePart(ref header, conn, payload, payload.Length);
                        else ProcessMessage(ref header, conn, payload, payload.Length);
                        return;
                    }
                    catch { Log.WriteNow("Failed to pass Encrypted Message. Wrong Key?"); return; }
                }
                else { Log.WriteNow("Failed to pass Encrypted Message. No Key Found For Connection"); return; }
            }
            // ========= End Encrpted Message ==================

            if (header.Parted) ProcessMesagePart(ref header, conn, payload, len);
            else ProcessMessage(ref header, conn, payload, len);
        }

        void ProcessMesagePart(ref Header header, Connection conn, byte[] payload, int len)
        {
            if (!conn.OngoingMessages.TryGetValue(header.ID, out OngoingMessage mes))
            {
                mes = new OngoingMessage(header.Channel, header.TotalParts, header.Length, header.ID);
                conn.OngoingMessages.Add(mes.ID, mes);
            }

            if (!mes.partsCollected[header.Part])
            {
                MessagePart part = MessagePart.Desserialize(payload, len);
                bool finished = mes.MessageRecieved(part, MaxPacketSize);

                if (finished)
                {
                    ProcessData(conn, mes.Data, mes.Data.Length);
                    conn.OngoingMessages.Remove(mes.ID);
                }
            }
            MessagePartConfirmation mpc = new MessagePartConfirmation(header.ID, header.Part);
            Send(conn, ref mpc);
        }

        void ProcessMessage(ref Header header, Connection conn, byte[] payload, int len)
        {
            switch (header.Channel)
            {
                case MessageTypes.Ping:
                    // Ping Test
                    rawServer.Send(conn, payload.SubArray(0, len));
                    break;

                // ======== Encypted Message ===============
                case MessageTypes.RSARegistration:
                    RSARegistration rsaMessage = RSARegistration.Desserialize(payload, len);
                    RSARegistration rsaReply;
                    switch (rsaMessage.step)
                    {
                        case RSARegistration.Step.InitalRequest:
                            rsaReply = new RSARegistration(RSARegistration.Step.ServerKey, RSAServerKeys.PublicBytes);
                            Send(conn, ref rsaReply);
                            break;
                        case RSARegistration.Step.ClientResponse:
                            rsaMessage.Decrypt(RSAServerKeys);

                            byte[] clientRSAKey = rsaMessage.Data;
                            // RSAPublicKeys[conn] = clientRSAKey;
                            AESKeys[conn] = "Some Generated Key"; // TODO: Generate this AESKey

                            rsaReply = new RSARegistration(RSARegistration.Step.AESKey, System.Text.Encoding.Unicode.GetBytes(AESKeys[conn]));
                            rsaReply.Encrypt(new RSAEncryption.RSAKeys(_public: clientRSAKey));
                            Send(conn, ref rsaReply);
                            break;
                    }
                    break;
                // ========= End Encrpted Message ==================

                default:
                    for (int i = 0; i < Handlers.Count; ++i)
                    {
                        Handler h = Handlers[i];
                        if (h.channel == header.Channel)
                        {
                            if (header.IsAsync) h.Call(conn, payload, len);
                            else SyncMessagesToProcess.Enqueue(new ServerSyncMessageCall(h, payload, len, conn));
                        }
                    }
                    break;
            }
        }
    }
}
