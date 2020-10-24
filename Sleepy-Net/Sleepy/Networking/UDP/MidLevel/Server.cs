#if SLEEPY_SERVER
using System;
using System.Net;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Sleepy.Security;
using static Sleepy.Net.NetworkingVars;
using Sleepy.Collections;

namespace Sleepy.Net.UDP
{
    public class Server
    {
        public readonly RawServer rawServer;
        public ushort MaxConnections => rawServer.MaxConnections;
        public SafeDictionary<EndPoint, Connection> Connections => rawServer.Connections;

        readonly List<Handler> Handlers;
        Dictionary<Connection, Dictionary<ushort, ServerMessagePartsActive>> ActiveMessageParts;
        ConcurrentQueue<ServerSyncMessageCall> SyncMessagesToProcess;

        // ======== Encypted Message ===============
        RSAEncryption.RSAKeys RSAServerKeys;
        Dictionary<Connection, string> AESKeys;
        // ========= End Encrpted Message ==================

        public RawServer.ConnectionMessage OnConnect;
        public RawServer.ConnectionMessage OnDisconnect;

        public RawServer.State state => rawServer.state;

#if SLEEPY_STATS
        public Stats stats => rawServer.stats;
#endif

        // =============== Setup ==================

        public Server(ushort Port, ushort maxConnections = 64)
        {
            rawServer = new RawServer(Port, maxConnections);
            rawServer.OnConnect += InternalOnConnect;
            rawServer.OnDisconnect += InternalOnDisconnect;
            rawServer.OnPacket += ProcessData;

            Handlers = new List<Handler>(100);
            ActiveMessageParts = new Dictionary<Connection, Dictionary<ushort, ServerMessagePartsActive>>();
            SyncMessagesToProcess = new ConcurrentQueue<ServerSyncMessageCall>();
            AESKeys = new Dictionary<Connection, string>();
        }

        void ResetData()
        {
            ActiveMessageParts.Clear();
            SyncMessagesToProcess = new ConcurrentQueue<ServerSyncMessageCall>();
            AESKeys.Clear();
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

        public void Start()
        {
            ResetData();
            rawServer.Start();

            // ======== Encypted Message ===============
#if SLEEPY_DEBUG
            Log.WriteNow("Server | Setting Up Encryption");
            RSAServerKeys = RSAEncryption.GenerateKeys(1024);
            Log.WriteNow("Server | Encryption Setup");
#else
            RSAServerKeys = Sleepy.Security.RSAEncryption.GenerateKeys(4096);
#endif
            // ========= End Encrpted Message ==================
        }

        public void Stop() => rawServer.Stop();
        public void Close() => rawServer.Close();

        void InternalOnConnect(Connection conn) => OnConnect?.Invoke(conn);
        void InternalOnDisconnect(Connection conn) => OnDisconnect?.Invoke(conn);

        // ================ Update ===============

        public void Update()
        {
            if (!SyncMessagesToProcess.IsEmpty)
            {
                while (SyncMessagesToProcess.TryDequeue(out ServerSyncMessageCall mess)) mess.Call();
            }

            foreach (KeyValuePair<Connection, Dictionary<ushort, ServerMessagePartsActive>> ConnectionsActiveMessages in ActiveMessageParts)
            {
                foreach (KeyValuePair<ushort, ServerMessagePartsActive> ActiveMessage in ConnectionsActiveMessages.Value)
                {
                    if (ActiveMessage.Value.Process())
                    {
                        ConnectionsActiveMessages.Value.Remove(ActiveMessage.Key);
                        break;
                    }
                }
            }
        }

        // ============== Send ==================

        public void InternalSend(EndPoint conn, byte[] data) => rawServer.Send(conn, data); // Not intended to be used

        public void Send<T>(Connection connection, T message) where T : IMessage => Send(connection, ref message);
        public void Send<T>(Connection connection, ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;

            byte[] data = MessageUtil.Serialize(ref message);
            BitConverter.GetBytes(data.Length).CopyTo(data, 6);

            rawServer.Send(connection.Conn, data);
        }

        public void SendMulti<T>(Connection[] connections, T message) where T : IMessage => SendMulti(connections, ref message);
        public void SendMulti<T>(Connection[] connections, ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;

            byte[] data = MessageUtil.Serialize(ref message);
            BitConverter.GetBytes(data.Length).CopyTo(data, 6);

            for (int i = 0; i < connections.Length; ++i)
            {
                rawServer.Send(connections[i].Conn, data);
            }
        }

        public void SendLarge<T>(Connection connection, T message, byte[] Data = null) where T : IMessage => SendLarge(connection, ref message, Data);
        public void SendLarge<T>(Connection connection, ref T message, byte[] Data = null) where T : IMessage
        {
            if (Data == null)
            {
                // Message not already serialized
                IMessage m = message;
                Data = MessageUtil.Serialize(ref m);
                BitConverter.GetBytes(Data.Length).CopyTo(Data, 6);
            }

            if (Data.Length < MaxPacketSize)
            {
                rawServer.Send(connection.Conn, Data);
                return;
            }

            message.TotalParts = (ushort)((Data.Length / MaxPacketSize) + 1);

            int remaining = Data.Length % MaxPacketSize;
            int amount = message.TotalParts - 1;
            ushort id = message.ID;

            ServerMessagePartsActive parts = new ServerMessagePartsActive(message.TotalParts, id, connection, this);

            // Issue to full Packet Size Parts
            for (ushort i = 0; i < amount; ++i)
            {
                MessagePart part = new MessagePart(message.Channel, Data.Length, i, message.TotalParts, id)
                {
                    Data = Data.SubArray(i * MaxPacketSize, MaxPacketSize)
                };
                parts.AddPart(MessageUtil.Serialize(ref part));
            }

            // Send the end Part that contains the rest of the data
            MessagePart endPart = new MessagePart(message.Channel, Data.Length, (ushort)amount, (ushort)(amount + 1), id)
            {
                Data = Data.SubArray(amount * MaxPacketSize, remaining)
            };
            parts.AddPart(MessageUtil.Serialize(ref endPart));

            if (!ActiveMessageParts.TryGetValue(connection, out Dictionary<ushort, ServerMessagePartsActive> activeMessgaes))
            {
                activeMessgaes = new Dictionary<ushort, ServerMessagePartsActive>();
                ActiveMessageParts[connection] = activeMessgaes;
            }
            activeMessgaes.Add(id, parts);
        }

        public void EncrptedSend<T>(Connection connection, T message) where T : IMessage => SendLarge(connection, ref message);
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

            byte[] data = MessageUtil.Serialize(ref aesMessage);
            BitConverter.GetBytes(data.Length).CopyTo(data, 6);

            rawServer.Send(connection.Conn, data);
        }

        public void EncrptedSendLarge<T>(Connection connection, T message) where T : IMessage => EncrptedSendLarge(connection, ref message);
        public void EncrptedSendLarge<T>(Connection connection, ref T message) where T : IMessage
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

        // =============== Process Data ===================

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
                    InternalSend(conn.Conn, payload.SubArray(0, len));
                    conn.FTT = Ping.Desserialize(payload, len).LastKnownFTT;
                    break;

                case MessageTypes.MessagePartConfirmation:
                    MessagePartConfirmation part = MessagePartConfirmation.Desserialize(payload, len);
                    if (ActiveMessageParts.TryGetValue(conn, out Dictionary<ushort, ServerMessagePartsActive> activeMessages))
                    {
                        if (activeMessages.TryGetValue(part.MessageID, out ServerMessagePartsActive activeMessage))
                        {
                            activeMessage.RecievedPart(part.PartNumber);
                        }
                    }
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
#endif