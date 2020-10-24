using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Sleepy.Streams;

namespace Sleepy.Net
{
    public interface IMessage
    {
        ushort Channel { get; set; }
        ushort Part { get; set; }
        ushort TotalParts { get; set; }
        int Length { get; set; }
        ushort ID { get; set; }
        bool IsAsync { get; set; }
    }

    public interface ISerialized
    {
        void Serialize(ReusableStream writer);
        void Deserialize(byte[] data, int len);
        T Deserialize<T>(ReusableStream reader) where T : IMessage;
    }

    public struct Header : IMessage
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public bool Parted => TotalParts > 1;

        public Header(ushort channel, int totalLength = 0, ushort part = 0, ushort totalParts = 1, ushort id = 0, bool isAsync = true)
        {
            Channel = channel;
            Part = part;
            TotalParts = totalParts;
            Length = totalLength;
            ID = id;
            IsAsync = isAsync;
        }
    }

    public struct Ping : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public float LastKnownFTT;

        public Ping(ushort id)
        {
            Channel = MessageTypes.Ping;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = id;
            IsAsync = true;
            LastKnownFTT = 0;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(LastKnownFTT);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            LastKnownFTT = reader.ReadSingle();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            Ping t = new Ping();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static Ping Desserialize(byte[] data, int len)
        {
            Ping t = new Ping();
            t.Deserialize(data, len);
            return t;
        }
    }

    public struct MessagePart : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public byte[] Data;

        public MessagePart(ushort msgType, int totalLength, ushort part = 0, ushort totalParts = 1, ushort id = 0)
        {
            Channel = msgType;
            Part = part;
            TotalParts = totalParts;
            Length = totalLength;
            ID = id;
            IsAsync = true;

            Data = new byte[0];
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(Data.Length);
            writer.Write(Data);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            int l = reader.ReadInt32();
            Data = new byte[l];
            reader.Read(data, 0, l);
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            MessagePart t = new MessagePart();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static MessagePart Desserialize(byte[] data, int len)
        {
            MessagePart t = new MessagePart();
            t.Deserialize(data, len);
            return t;
        }
    }

    public struct MessagePartConfirmation : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public ushort MessageID;
        public ushort PartNumber;

        public MessagePartConfirmation(ushort msgID, ushort part)
        {
            Channel = MessageTypes.MessagePartConfirmation;
            Part = part;
            TotalParts = 1;
            Length = 0;
            ID = msgID;
            IsAsync = true;

            MessageID = msgID;
            PartNumber = part;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(MessageID);
            writer.Write(PartNumber);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            MessageID = reader.ReadUInt16();
            PartNumber = reader.ReadUInt16();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            MessagePartConfirmation t = new MessagePartConfirmation();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static MessagePartConfirmation Desserialize(byte[] data, int len)
        {
            MessagePartConfirmation t = new MessagePartConfirmation();
            t.Deserialize(data, len);
            return t;
        }
    }

    // ===================== Message Utils ===================================

    public static class MessageUtil
    {
        [ThreadStatic] static ReusableStream writeStream;
        [ThreadStatic] static ReusableStream readStream;
        [ThreadStatic] static ReusableStream reusableStream;

        public static Dictionary<Type, ISerialized> MessageTypes = new Dictionary<Type, ISerialized>() { };
        public const int HeaderSize = 13;

        public static byte[] Serialize<T>(ref T message) where T : IMessage
        {
            if (writeStream == null) writeStream = new ReusableStream(new byte[64512], 0, 64512, true);

            writeStream.ResetForReading();

            writeStream.Write(message.Channel);
            writeStream.Write(message.Part);
            writeStream.Write(message.TotalParts);
            writeStream.Write(message.Length);
            writeStream.Write(message.ID);
            writeStream.Write(message.IsAsync);

            if (message is ISerialized sMessage) sMessage.Serialize(writeStream);
            else writeStream.Write(JsonUtility.ToJson(message));

            return writeStream.Data.SubArray(0, (int)writeStream.Position);
        }

        public static void Serialize<T>(ref T message, byte[] buffer, out int len) where T : IMessage
        {
            if (reusableStream == null) reusableStream = new ReusableStream();

            reusableStream.ReplaceData(buffer, 0, buffer.Length, false);

            reusableStream.Write(message.Channel);
            reusableStream.Write(message.Part);
            reusableStream.Write(message.TotalParts);
            reusableStream.Write(message.Length);
            reusableStream.Write(message.ID);
            reusableStream.Write(message.IsAsync);

            if (message is ISerialized sMessage) sMessage.Serialize(reusableStream);
            else reusableStream.Write(JsonUtility.ToJson(message));

            len = (int)reusableStream.Position;
        }

        public static T Deserialize<T>(byte[] data, int len) where T : IMessage, new()
        {
            if (readStream == null) readStream = new ReusableStream();
            readStream.ReplaceData(data, 0, len, false);

            if (MessageTypes.TryGetValue(typeof(T), out ISerialized sBase))
            {
                return sBase.Deserialize<T>(readStream);
            }
            else if (typeof(T).GetInterfaces().Contains(typeof(ISerialized)))
            {
                sBase = (ISerialized)new T();
                MessageTypes[typeof(T)] = sBase;
                return sBase.Deserialize<T>(readStream);
            }

            ushort channel = readStream.ReadUInt16();
            ushort part = readStream.ReadUInt16();
            ushort totalParts = readStream.ReadUInt16();
            int length = readStream.ReadInt32();
            ushort id = readStream.ReadUInt16();
            bool isAsync = readStream.ReadBoolean();

            T Mes;
            try
            {
                Mes = JsonUtility.FromJson<T>(readStream.ReadString());
                Mes.Channel = channel;
                Mes.Part = part;
                Mes.TotalParts = totalParts;
                Mes.Length = length;
                Mes.ID = id;
                Mes.IsAsync = isAsync;
            }
            catch
            {
                Mes = new T
                {
                    Channel = channel,
                    Part = part,
                    TotalParts = totalParts,
                    Length = length,
                    ID = id,
                    IsAsync = isAsync
                };
            }

            return Mes;
        }

        public static Header DeserializeHeader(byte[] data, int len)
        {
            if (readStream == null) readStream = new ReusableStream();
            readStream.ReplaceData(data, 0, len, false);

            ushort channel = readStream.ReadUInt16();
            ushort part = readStream.ReadUInt16();
            ushort totalParts = readStream.ReadUInt16();
            int length = readStream.ReadInt32();
            ushort id = readStream.ReadUInt16();
            bool isAsync = readStream.ReadBoolean();

            return new Header(channel, length, part, totalParts, id, isAsync);
        }

        public static void ReplyWith<T, V>(this T req, ref V resp) where T : IMessage where V : IMessage
        {
            resp.ID = req.ID;
            resp.IsAsync = req.IsAsync;
            resp.Channel = req.Channel;
        }
    }

    // ================== Encypted Messages ==========================

    public struct RSARegistration : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public enum Step { InitalRequest, ServerKey, ClientResponse, AESKey }
        public Step step;
        public byte[] Data;

        public RSARegistration(Step s, byte[] data)
        {
            Channel = Net.MessageTypes.RSARegistration;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = true;

            step = s;
            Data = data;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write((byte)step);
            writer.Write(Data.Length);
            writer.Write(Data);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            step = (Step)reader.ReadByte();
            int l = reader.ReadInt32();
            Data = new byte[l];
            reader.Read(Data, 0, l);
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            RSARegistration t = new RSARegistration();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static RSARegistration Desserialize(byte[] data, int len)
        {
            RSARegistration t = new RSARegistration();
            t.Deserialize(data, len);
            return t;
        }

        public void Encrypt(Security.RSAEncryption.RSAKeys PublicKey) => Data = PublicKey.Encrypt(Data);
        public void Decrypt(Security.RSAEncryption.RSAKeys PrivateKey) => Data = PrivateKey.Decrypt(Data);
    }

    public struct RSAMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public byte[] PublicKey;
        public byte[] Data;

        public RSAMessage(byte[] publicKey, byte[] data)
        {
            Channel = Net.MessageTypes.RSAMessage;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = true;

            PublicKey = publicKey;
            Data = data;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(PublicKey.Length);
            writer.Write(PublicKey);
            writer.Write(Data.Length);
            writer.Write(Data);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            int l = reader.ReadInt32();
            PublicKey = new byte[l];
            reader.Read(PublicKey, 0, l);

            l = reader.ReadInt32();
            Data = new byte[l];
            reader.Read(Data, 0, l);
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            RSAMessage t = new RSAMessage();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static RSAMessage Desserialize(byte[] data, int len)
        {
            RSAMessage t = new RSAMessage();
            t.Deserialize(data, len);
            return t;
        }

        public void Encrypt(Security.RSAEncryption.RSAKeys PublicKey) => Data = PublicKey.Encrypt(Data);
        public void Decrypt(Security.RSAEncryption.RSAKeys PrivateKey) => Data = PrivateKey.Decrypt(Data);
    }

    public struct AESMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public byte[] message;

        public AESMessage(IMessage mes)
        {
            Channel = Net.MessageTypes.AESMessage;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = true;

            message = MessageUtil.Serialize(ref mes);
        }

        public AESMessage(byte[] mes)
        {
            Channel = Net.MessageTypes.AESMessage;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = true;

            message = mes;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(message.Length);
            writer.Write(message);
        }

        public void Deserialize(byte[] data, int len)
        {
            ReusableStream reader = new ReusableStream(data, 0, len, false);
            Channel = reader.ReadUInt16();
            Part = reader.ReadUInt16();
            TotalParts = reader.ReadUInt16();
            Length = reader.ReadInt32();
            ID = reader.ReadUInt16();
            IsAsync = reader.ReadBoolean();

            int l = reader.ReadInt32();
            message = new byte[l];
            reader.Read(message, 0, l);
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            AESMessage t = new AESMessage();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }

        public static AESMessage Desserialize(byte[] data, int len)
        {
            AESMessage t = new AESMessage();
            t.Deserialize(data, len);
            return t;
        }

        public void Encrypt(string Key) => message = Security.AESEncryption.Encrypt(message, Key);
        public void Decrypt(string Key) => message = Security.AESEncryption.Decrypt(message, Key);
    }
}