using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Sleepy.Security;
using static Sleepy.Net.NetworkingVars;

namespace Sleepy.Net.UDP
{
    public class Client
    {
        RawClient rawClient;
        Dictionary<ushort, OngoingMessage> OngoingMessages;
        Dictionary<ushort, ClientMessagePartsActive> ActiveMessageParts; // NOTE: This can probably be replaced with something, as we now have a queue for sending packets
        ConcurrentQueue<ClientSyncMessageCall> SyncMessagesToProcess;

        List<Handler> Handlers;
        List<Handler> DynamicMessagesInProgresses;

        RSAEncryption.RSAKeys ServerPublicRSAKey;
        RSAEncryption.RSAKeys RSAKeys;
        string AESKey;
        public bool EncryptionSetup => !string.IsNullOrEmpty(AESKey);
        public bool AutoSetupEncryption = false;

        ushort _nextID = 0;
        ushort NextID { get { _nextID++; if (_nextID == ushort.MaxValue) _nextID = 3; return _nextID; } }

        public RawClient.ConnectionMessage OnConnect;
        public RawClient.ConnectionMessage OnDisconnect;

        public RawClient.State state => rawClient.state;
        public bool Connected => rawClient.state == RawClient.State.Connected;

#if SLEEPY_STATS
        public Stats stats => rawClient.stats;
#endif

        // =============== Setup ==================

        public Client(string IP, ushort Port)
        {
            rawClient = new RawClient(IP, Port);
            rawClient.OnConnect += InternalOnConnect;
            rawClient.OnDisconnect += InternalOnDisconnect;
            rawClient.OnPacket += ProcessData;

            Handlers = new List<Handler>();
            OngoingMessages = new Dictionary<ushort, OngoingMessage>();
            ActiveMessageParts = new Dictionary<ushort, ClientMessagePartsActive>();
            DynamicMessagesInProgresses = new List<Handler>();
            SyncMessagesToProcess = new ConcurrentQueue<ClientSyncMessageCall>();
        }

        void ResetData()
        {
            OngoingMessages.Clear();
            ActiveMessageParts.Clear();
            DynamicMessagesInProgresses.Clear();
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

        // ============== Connection =================

        public void Connect()
        {
            ResetData();

            rawClient.Connect();
        }

        public void Disconnect() => rawClient.Disconnect();
        public void ForceDisconnect(bool notifyServer = true) => rawClient.ForceDisconnect(notifyServer);
        public void Close() => rawClient.Close();

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
            if (state != RawClient.State.Connected) return;
#if SLEEPY_DEBUG
            RSAKeys = RSAEncryption.GenerateKeys(512);
#else
            RSAKeys = Sleepy.Security.RSAEncryption.GenerateKeys(2048);
#endif
            Send(new RSARegistration(RSARegistration.Step.InitalRequest, new byte[0])); // Kick off the Encrption Handshake
        }

        // ================= Update ==================

        public void Update()
        {
            if (state == RawClient.State.Terminated) return;

            if (LastPingSendTtime.AddSeconds(10) < DateTime.Now && !PingStopwatch.IsRunning) AsyncFullPing();

            if (!SyncMessagesToProcess.IsEmpty)
            {
                while (SyncMessagesToProcess.TryDequeue(out ClientSyncMessageCall mess)) mess.Call();
            }

            foreach (KeyValuePair<ushort, ClientMessagePartsActive> ActiveMessage in ActiveMessageParts)
            {
                if (ActiveMessage.Value.Process())
                {
                    ActiveMessageParts.Remove(ActiveMessage.Key);
                    break;
                }
            }
        }

        // ============== Processing Data ===============

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

            MessagePartConfirmation mspc = new MessagePartConfirmation(header.ID, header.Part);
            Send(ref mspc);
        }

        void ProcessMessage(ref Header header, byte[] payload, int len)
        {
            switch (header.Channel)
            {
                case MessageTypes.MessagePartConfirmation:
                    MessagePartConfirmation part = MessagePartConfirmation.Desserialize(payload, len);

                    if (ActiveMessageParts.TryGetValue(part.MessageID, out ClientMessagePartsActive activeMessage))
                    {
                        activeMessage.RecievedPart(part.PartNumber);
                    }
                    break;

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
                            Send(ref rsaReply);
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

        // =============== Send ==================

        public void InternalSend(byte[] data) => rawClient.Send(data); // Not intended to be used

        public void Send<T>(T message) where T : IMessage => Send(ref message);
        public void Send<T>(ref T message) where T : IMessage
        {
            message.TotalParts = 1;
            message.Part = 0;
            message.ID = message.ID != 0 ? message.ID : NextID;

            byte[] data = MessageUtil.Serialize(ref message);
            BitConverter.GetBytes(data.Length).CopyTo(data, 6);

            rawClient.Send(data);
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

            ClientMessagePartsActive parts = new ClientMessagePartsActive(message.TotalParts, id, this);

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
            MessagePart endPart = new MessagePart(message.Channel, Data.Length, (ushort)amount, message.TotalParts, id)
            {
                Data = Data.SubArray(amount * MaxPacketSize, remaining)
            };
            parts.AddPart(MessageUtil.Serialize(ref endPart));

            ActiveMessageParts.Add(id, parts);
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

            byte[] data = MessageUtil.Serialize(ref aesMessage);
            BitConverter.GetBytes(data.Length).CopyTo(data, 6);

            rawClient.Send(data);
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
            byte[] data = MessageUtil.Serialize(ref message);

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
            ClientMessagePartsActive cachedMessageOut;

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

            public float SendProgress
            {
                get
                {
                    if (cachedMessageOut == null)
                    {
                        if (client.ActiveMessageParts.TryGetValue(Request.ID, out cachedMessageOut))
                        {
                            return cachedMessageOut.Progress;
                        }
                    }
                    else
                    {
                        return cachedMessageOut.Progress;
                    }

                    return 0;
                }
            }

            public bool IsValid => ResponseRecieved && Response != null;
            public bool ResponseRecieved;
            public override bool keepWaiting { get { if (timeout != -1 && timeSent + timeout < Time.unscaledTime) return false; return ResponseRecieved; } }

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
                ResponseRecieved = true;
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
            UnityEngine.Ping p = new UnityEngine.Ping(rawClient.serverEndPoint.Address.ToString());
            while (!p.isDone) { }
            UTT = p.time;

            System.Net.NetworkInformation.Ping p2 = new System.Net.NetworkInformation.Ping();
            System.Net.NetworkInformation.PingReply r = p2.Send(rawClient.serverEndPoint.Address);

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