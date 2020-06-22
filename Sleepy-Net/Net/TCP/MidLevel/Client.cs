using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Sleepy.Security;
using static Sleepy.Net.NetowrkingVars;

namespace Sleepy.Net.TCP
{
    public class Client
    {
        public RawClient rawClient;
        public bool Connected => rawClient.Connected;

        [ThreadStatic] static byte[] header;
        [ThreadStatic] static byte[] payload;

        Dictionary<ushort, OngoingMessage> OngoingMessages;
        ConcurrentQueue<ClientSyncMessageCall> SyncMessagesToProcess;

        List<Handler> Handlers;
        List<Handler> DynamicMessagesInProgresses;

        RSAEncryption.RSAKeys ServerPublicRSAKey;
        RSAEncryption.RSAKeys RSAKeys;
        string AESKey;
        public bool EncryptionSetup => !string.IsNullOrEmpty(AESKey);
        public bool AutoSetupEncryption = false;
        public bool AutoReconnectOnDisconnect = true;

        ushort _nextID = 0;
        ushort NextID
        {
            get
            {
                _nextID++; if (_nextID == ushort.MaxValue) _nextID = 3; return _nextID;
            }
        }

        public delegate void ConnectionMessage();
        public ConnectionMessage OnConnect;
        public ConnectionMessage OnDisconnect;

        // =============== Setup ==================

        public Client(string ip, ushort port)
        {
            rawClient = new RawClient(ip, port);
            rawClient.OnConnect += InternalOnConnect;
            rawClient.OnDisconnect += InternalOnDisconnect;
            rawClient.OnPacket += ProcessData;

            Handlers = new List<Handler>();
        }

        void ResetData()
        {
            OngoingMessages = new Dictionary<ushort, OngoingMessage>();
            DynamicMessagesInProgresses = new List<Handler>();
            SyncMessagesToProcess = new ConcurrentQueue<ClientSyncMessageCall>();
        }

        // =============== Binding ====================

        public void Bind<T>(ushort msgType, Action<T> callback) where T : IMessage, new()
        {
            Handlers.Add(new Handler<T>() { channel = msgType, Callback = callback });
        }

        public void Unbind<T>(ushort msgType, Action<T> callback) where T : IMessage, new()
        {
            foreach (Handler h in Handlers)
            {
                if (h.channel == msgType)
                {
                    Handler<T> converted = h as Handler<T>;
                    if (converted.Callback == callback)
                    {
                        Handlers.Remove(h);
                        return;
                    }
                }
            }
        }

        // =============== Connect ==================

        public void Connect() { ResetData(); rawClient.Connect(); }
        public void Disconnect() => rawClient.Disconnect();

        void InternalOnConnect()
        {
            if (AutoSetupEncryption) SetupEncryption();
            OnConnect?.Invoke();
        }

        void InternalOnDisconnect()
        {
            OnDisconnect?.Invoke();
        }

        // ================== Encyption ================

        void SetupEncryption()
        {
            if (!Connected) return;
#if ZT_DEBUG
            RSAKeys = RSAEncryption.GenerateKeys(512);
#else
            RSAKeys = Sleepy.Security.RSAEncryption.GenerateKeys(2048);
#endif
            RSARegistration rsar = new RSARegistration(RSARegistration.Step.InitalRequest, new byte[0]);
            rawClient.Send(MessageUtil.Serialize(ref rsar)); // Kick off the Encrption Handshake
        }

        // =============== Recv ===================

        void ProcessData(byte[] payload, int len)
        {
            Header header = MessageUtil.DeserializeHeader(payload, len);

            // ======== Encypted Message ===============
            if (header.Channel == MessageTypes.AESMessage)
            {
                try
                {
                    AESMessage message = AESMessage.Desserialize(payload, len);
                    message.Decrypt(AESKey);
                    payload = message.message;
                    header = MessageUtil.DeserializeHeader(payload, payload.Length);

                    if (header.Parted) ProcessMessagePart(ref header, payload, payload.Length);
                    else ProcessMessage(ref header, payload, payload.Length);

                    return;
                }
                catch { Log.WriteNow("Failed to pass Encrypted Message. Wrong Key?"); return; }
            }
            // ========= End Encrpted Message ==================

            if (header.Parted) ProcessMessagePart(ref header, payload, len);
            else ProcessMessage(ref header, payload, len);
        }

        void ProcessMessagePart(ref Header header, byte[] payload, int len)
        {
            if (!OngoingMessages.TryGetValue(header.ID, out OngoingMessage mes))
            {
                mes = new OngoingMessage(header.Channel, header.TotalParts, header.Length, header.ID);
                OngoingMessages.Add(mes.ID, mes);
            }

            if (!mes.partsCollected[header.Part])
            {
                MessagePart part = MessagePart.Desserialize(payload, len);
                bool finished = mes.MessageRecieved(part, MaxPacketSize);

                if (finished)
                {
                    ProcessData(mes.Data, mes.Data.Length);
                    OngoingMessages.Remove(mes.ID);
                }
            }

            Send(new MessagePartConfirmation(header.ID, header.Part));
        }

        void ProcessMessage(ref Header header, byte[] payload, int len)
        {
            switch (header.Channel)
            {
                // ======== Encypted Message ===============
                case MessageTypes.RSARegistration:
                    RSARegistration rsaMessage = RSARegistration.Desserialize(payload, len);
                    RSARegistration rsaReply;
                    switch (rsaMessage.step)
                    {
                        case RSARegistration.Step.ServerKey:
                            ServerPublicRSAKey = new RSAEncryption.RSAKeys(_public: rsaMessage.Data);

                            rsaReply = new RSARegistration(RSARegistration.Step.ClientResponse, RSAKeys.PublicBytes);
                            rsaReply.Encrypt(ServerPublicRSAKey);
                            Send(rsaReply);
                            break;
                        case RSARegistration.Step.AESKey:
                            rsaMessage.Decrypt(RSAKeys);

                            AESKey = System.Text.Encoding.Unicode.GetString(rsaMessage.Data);
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
                            if (header.IsAsync) h.Call(payload, len);
                            else SyncMessagesToProcess.Enqueue(new ClientSyncMessageCall(h, payload, len));
                        }
                    }

                    for (int i = DynamicMessagesInProgresses.Count - 1; i >= 0; --i)
                    {
                        Handler dh = DynamicMessagesInProgresses[i];

                        if (dh.MessageID == header.ID)
                        {
                            if (header.IsAsync) dh.Call(payload, len);
                            else SyncMessagesToProcess.Enqueue(new ClientSyncMessageCall(dh, payload, len));
                            DynamicMessagesInProgresses.RemoveAt(i);
                        }
                    }
                    break;
            }
        }

        // ================= Send ===================

        public void Send<T>(T message) where T : IMessage => Send(ref message);
        public void Send<T>(ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;
            message.ID = message.ID != 0 ? message.ID : NextID;

            if (payload == null) payload = new byte[MaxBufferSize];

            MessageUtil.Serialize(ref message, payload, out int len);
            BitConverter.GetBytes(len).CopyTo(payload, 6);

            rawClient.Send(payload.SubArray(0, len));
        }

        public void SendLarge<T>(T message, byte[] Data = null) where T : IMessage => SendLarge(ref message, Data);
        public void SendLarge<T>(ref T message, byte[] Data = null) where T : IMessage
        {
            message.ID = message.ID != 0 ? message.ID : NextID;

            if (Data == null)
            {
                // Message not already serialized
                Data = MessageUtil.Serialize(ref message);
                BitConverter.GetBytes(Data.Length).CopyTo(Data, 6);
            }

            if (Data.Length < MaxPacketSize)
            {
                rawClient.Send(Data);
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
                rawClient.Send(MessageUtil.Serialize(ref part));
            }

            // Send the end Part that contains the rest of the data
            MessagePart endPart = new MessagePart(message.Channel, Data.Length, (ushort)amount, message.TotalParts, id)
            {
                Data = Data.SubArray(amount * MaxPacketSize, remaining)
            };
            rawClient.Send(MessageUtil.Serialize(ref endPart));
        }

        public void EncrptedSend<T>(T message) where T : IMessage => EncrptedSend(ref message);
        public void EncrptedSend<T>(ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;
            message.ID = message.ID != 0 ? message.ID : NextID;

            AESMessage aesMessage = new AESMessage(message)
            {
                ID = message.ID
            };
            aesMessage.Encrypt(AESKey);

            byte[] buffer = MessageUtil.Serialize(ref aesMessage);
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 6);

            rawClient.Send(buffer);
        }

        public void EncrptedSendLarge<T>(T message, byte[] Data = null) where T : IMessage => EncrptedSendLarge(ref message, Data);
        public void EncrptedSendLarge<T>(ref T message, byte[] Data = null) where T : IMessage
        {
            message.ID = message.ID != 0 ? message.ID : NextID;

            if (Data == null)
            {
                // Message not already serialized
                Data = MessageUtil.Serialize(ref message);
                BitConverter.GetBytes(Data.Length).CopyTo(Data, 6);
            }

            AESMessage aesMessage = new AESMessage(Data)
            {
                ID = message.ID
            };
            aesMessage.Encrypt(AESKey);
            byte[] data = MessageUtil.Serialize(ref aesMessage);

            SendLarge(ref aesMessage, data);
        }

        // ============= Dynamic Co-routine Based Messaging =================

        public class DynamicRequest<T, V> : CustomYieldInstruction where T : IMessage where V : IMessage, new()
        {
            public T Request;
            public V Response;
            public float timeout = 5f;

            readonly float timeSent;
            readonly Client client;
            OngoingMessage cachedMessage;

            public float RecvProgress
            {
                get
                {
                    if (cachedMessage == null)
                    {
                        if (client.OngoingMessages.TryGetValue(Request.ID, out cachedMessage))
                        {
                            return cachedMessage.Progress;
                        }
                    }
                    else
                    {
                        return cachedMessage.Progress;
                    }

                    return 0;
                }
            }

            public bool IsValid => Response != null;
            public override bool keepWaiting { get { if (timeout != -1 && timeSent + timeout < Time.unscaledTime) return false; return Response == null; } }

            public DynamicRequest(Client client, ref T request, bool Encrypted = false, bool Large = false)
            {
                Request = request;
                this.client = client;

                if (Large)
                {
                    if (Encrypted) client.EncryptedSendLarge<T, V>(ref request, ResponseRecived);
                    else client.SendLarge<T, V>(ref request, ResponseRecived);
                }
                else
                {
                    if (Encrypted) client.EncryptedSend<T, V>(ref request, ResponseRecived);
                    else client.Send<T, V>(ref request, ResponseRecived);
                }

                timeSent = Time.unscaledTime;
            }

            void ResponseRecived(V resp)
            {
                Response = resp;
            }
        }

        public DynamicRequest<T, V> SendRequest<T, V>(T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, false, false);
        public DynamicRequest<T, V> SendRequest<T, V>(ref T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, false, false);

        public DynamicRequest<T, V> SendLargeRequest<T, V>(T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, false, true);
        public DynamicRequest<T, V> SendLargeRequest<T, V>(ref T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, false, true);

        public DynamicRequest<T, V> SendEncryptedRequest<T, V>(T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, true);
        public DynamicRequest<T, V> SendEncryptedRequest<T, V>(ref T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, true);

        public DynamicRequest<T, V> SendLargeEncryptedRequest<T, V>(T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, true, true);
        public DynamicRequest<T, V> SendLargeEncryptedRequest<T, V>(ref T message) where T : IMessage where V : IMessage, new() => new DynamicRequest<T, V>(this, ref message, true, true);

        public void Send<T, V>(T message, Action<V> Callback) where T : IMessage where V : IMessage, new() => Send(ref message, Callback);
        public void Send<T, V>(ref T message, Action<V> Callback) where T : IMessage where V : IMessage, new()
        {
            Send(ref message);

            DynamicMessagesInProgresses.Add(new Handler<V>() { Callback = Callback, MessageID = message.ID, channel = message.Channel });
        }

        public void SendLarge<T, V>(T message, Action<V> Callback, byte[] Data = null) where T : IMessage where V : IMessage, new() => SendLarge(ref message, Callback);
        public void SendLarge<T, V>(ref T message, Action<V> Callback, byte[] Data = null) where T : IMessage where V : IMessage, new()
        {
            SendLarge(ref message, Data);

            DynamicMessagesInProgresses.Add(new Handler<V>() { Callback = Callback, MessageID = message.ID, channel = message.Channel });
        }

        public void EncryptedSend<T, V>(T message, Action<V> Callback) where T : IMessage where V : IMessage, new() => EncryptedSend(ref message, Callback);
        public void EncryptedSend<T, V>(ref T message, Action<V> Callback) where T : IMessage where V : IMessage, new()
        {
            EncrptedSend(ref message);

            DynamicMessagesInProgresses.Add(new Handler<V>() { Callback = Callback, MessageID = message.ID, channel = message.Channel });
        }

        public void EncryptedSendLarge<T, V>(T message, Action<V> Callback) where T : IMessage where V : IMessage, new() => EncryptedSendLarge(ref message, Callback);
        public void EncryptedSendLarge<T, V>(ref T message, Action<V> Callback) where T : IMessage where V : IMessage, new()
        {
            EncrptedSendLarge(ref message);

            DynamicMessagesInProgresses.Add(new Handler<V>() { Callback = Callback, MessageID = message.ID, channel = message.Channel });
        }

        // ================ Ping ====================

        readonly Stopwatch PingStopwatch = new Stopwatch();
        public bool PingInProgress;
        DateTime LastPingSendTtime;
        public float FTT { get; private set; } // Full Trip Time - Fully Processed Packet
        public float RTT { get; private set; } // Round Trip Time - Raw Ping
        public float UTT { get; private set; } // Unity Trip Time

        public int SyncPing()
        {
            UnityEngine.Ping p = new UnityEngine.Ping(rawClient.IP);
            while (!p.isDone) { }
            UTT = p.time;

            System.Net.NetworkInformation.Ping p2 = new System.Net.NetworkInformation.Ping();
            System.Net.NetworkInformation.PingReply r = p2.Send(rawClient.IP);

            RTT = r.RoundtripTime;

            return (int)r.RoundtripTime;
        }

        public void AsyncFullPing()
        {
            PingInProgress = true;
            Send<Ping, Ping>(new Ping(0), PingReturn);
            PingStopwatch.Restart();
        }

        void PingReturn(Ping resp)
        {
            PingStopwatch.Stop();
            LastPingSendTtime = DateTime.Now;

            RTT = (PingStopwatch.ElapsedTicks / 10000f);
            PingInProgress = false;
        }
    }
}
