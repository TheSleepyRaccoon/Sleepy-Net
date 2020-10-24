using Sleepy.Net.TCP;
using Sleepy.Streams;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sleepy.Net
{
    public partial class MessageTypes
    {
        public const ushort FullServerMessage = 10;
        public const ushort FullClientMessage = 11;

        public const ushort ClientRegistrationMessage = 12;

        public const ushort GetCachedObjectsMessage = 13;
        public const ushort CreateObjectMessage = 14;
        public const ushort DestroyObjectMessage = 15;

        public const ushort LoadSceneMessage = 16;
        public const ushort SceneSyncMessage = 17;
        public const ushort LoadSceneCompleteMessage = 18;

        public const ushort PlayerInfoSync = 19;
    }


    public struct FullSyncMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get { return false; } set { } }

        public class NetworkedObjectData
        {
            public Dictionary<ulong, byte[]> Data = new Dictionary<ulong, byte[]>();
        }

        public ulong messageNum;
        public NetworkedObjectData networkData;
        public bool force;

        public FullSyncMessage(ushort channel = MessageTypes.FullClientMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            networkData = new NetworkedObjectData();
            messageNum = 0;
            force = false;
            IsAsync = false;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(messageNum);
            writer.Write(force);
            writer.Write(networkData.Data.Count);
            foreach (KeyValuePair<ulong, byte[]> pair in networkData.Data)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.Length);
                writer.Write(pair.Value);
            }
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

            messageNum = reader.ReadUInt64();
            force = reader.ReadBoolean();

            int numOfItems = reader.ReadInt32();
            for (int i = 0; i < numOfItems; ++i)
            {
                ulong key = reader.ReadUInt64();
                int valueLen = reader.ReadInt32();
                byte[] objData = new byte[valueLen];
                reader.Read(objData, 0, valueLen);

                networkData.Data.Add(key, objData);
            }
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            FullSyncMessage t = new FullSyncMessage(MessageTypes.FullClientMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct ClientRegisterRequestMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public string playerName;

        public ClientRegisterRequestMessage(ushort channel = MessageTypes.ClientRegistrationMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            playerName = "";
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(playerName);
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

            playerName = reader.ReadString();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            ClientRegisterRequestMessage t = new ClientRegisterRequestMessage();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct ClientRegisterResponseMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public bool Accepted;
        public int PlayerID;
        public string playerName;

        public ClientRegisterResponseMessage(ushort channel = MessageTypes.ClientRegistrationMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            Accepted = false;
            PlayerID = -1;
            playerName = "";
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(Accepted);
            writer.Write(PlayerID);
            writer.Write(playerName);
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

            Accepted = reader.ReadBoolean();
            PlayerID = reader.ReadInt32();
            playerName = reader.ReadString();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            ClientRegisterResponseMessage t = new ClientRegisterResponseMessage();
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct GetCachedObjectsRequestMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public enum SpawnType { Prefab, Player, Primitive }

        public int owner;

        public GetCachedObjectsRequestMessage(ushort channel = MessageTypes.GetCachedObjectsMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            owner = -1;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(owner);
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

            owner = reader.ReadInt32();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            GetCachedObjectsRequestMessage t = new GetCachedObjectsRequestMessage(MessageTypes.GetCachedObjectsMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct CreateObjectRequestMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public enum SpawnType { Prefab, Player, Primitive }

        public int playerRequested;
        public int playerToOwn;
        public bool perminant;
        public SpawnType spawnType;
        public string prefabName;

        public CreateObjectRequestMessage(ushort channel = MessageTypes.CreateObjectMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            playerRequested = -1;
            playerToOwn = -1;
            prefabName = "";
            spawnType = SpawnType.Prefab;
            perminant = true;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(playerRequested);
            writer.Write(playerToOwn);
            writer.Write(prefabName);
            writer.Write((int)spawnType);
            writer.Write(perminant);
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

            playerRequested = reader.ReadInt32();
            playerToOwn = reader.ReadInt32();
            prefabName = reader.ReadString();
            spawnType = (SpawnType)reader.ReadInt32();
            perminant = reader.ReadBoolean();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            CreateObjectRequestMessage t = new CreateObjectRequestMessage(MessageTypes.CreateObjectMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct CreateObjectResponseMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public enum SpawnType { Prefab, Player, Primitive }

        public int owner;
        public byte[] networkedObjectData;
        public SpawnType spawnType;
        public string prefabName;

        public CreateObjectResponseMessage(ushort channel = MessageTypes.CreateObjectMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            owner = -1;
            networkedObjectData = new byte[0];
            prefabName = "";
            spawnType = SpawnType.Prefab;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(owner);

            writer.Write(networkedObjectData.Length);
            writer.Write(networkedObjectData);

            writer.Write(prefabName);
            writer.Write((int)spawnType);
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

            owner = reader.ReadInt32();

            int networkedObjectDataLen = reader.ReadInt32();
            networkedObjectData = new byte[networkedObjectDataLen];
            reader.Read(networkedObjectData, 0, networkedObjectDataLen);

            prefabName = reader.ReadString();
            spawnType = (SpawnType)reader.ReadInt32();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            CreateObjectResponseMessage t = new CreateObjectResponseMessage(MessageTypes.CreateObjectMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }


    public struct DestroyObjectsRequestMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public enum SpawnType { Prefab, Player, Primitive }

        public int playerRequested;
        public ulong networkID;

        public DestroyObjectsRequestMessage(ushort channel = MessageTypes.DestroyObjectMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            playerRequested = -1;
            networkID = 0;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(playerRequested);
            writer.Write(networkID);
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

            playerRequested = reader.ReadInt32();
            networkID = reader.ReadUInt64();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            DestroyObjectsRequestMessage t = new DestroyObjectsRequestMessage(MessageTypes.DestroyObjectMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct DestroyObjectsResponseMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public List<ulong> networkIDs;

        public DestroyObjectsResponseMessage(ushort channel = MessageTypes.DestroyObjectMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            networkIDs = new List<ulong>();
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(networkIDs.Count);
            for(int i = 0; i < networkIDs.Count; ++i)
            {
                writer.Write(networkIDs[i]);
            }
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

            int num = reader.ReadInt32();
            networkIDs = new List<ulong>(num);
            for (int i = 0; i < num; ++i)
            {
                networkIDs.Add(reader.ReadUInt64());
            }
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            DestroyObjectsResponseMessage t = new DestroyObjectsResponseMessage(MessageTypes.DestroyObjectMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct LoadSceneMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public string SceneName;
        public bool Addative;

        public LoadSceneMessage(ushort channel = MessageTypes.LoadSceneMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            SceneName = "";
            Addative = false;
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(SceneName);
            writer.Write(Addative);
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

            SceneName = reader.ReadString();
            Addative = reader.ReadBoolean();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            LoadSceneMessage t = new LoadSceneMessage(MessageTypes.LoadSceneMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct LoadSceneCompleteMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public string SceneName;

        public LoadSceneCompleteMessage(ushort channel = MessageTypes.LoadSceneCompleteMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            SceneName = "";
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(SceneName);
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

            SceneName = reader.ReadString();
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            LoadSceneCompleteMessage t = new LoadSceneCompleteMessage(MessageTypes.LoadSceneCompleteMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct SceneSyncMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }


        public string sceneName;
        public Dictionary<ulong, byte[]> sceneMapping;

        public SceneSyncMessage(ushort channel = MessageTypes.SceneSyncMessage)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            sceneMapping = new Dictionary<ulong, byte[]>();
            sceneName = "";
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(sceneName);
            writer.Write(sceneMapping.Count);
            foreach (var mapping in sceneMapping)
            {
                writer.Write(mapping.Key);
                writer.Write(mapping.Value.Length);
                writer.Write(mapping.Value);
            }
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

            sceneName = reader.ReadString();
            int mappingLen = reader.ReadInt32();

            sceneMapping = new Dictionary<ulong, byte[]>(mappingLen);
            for (int i = 0; i < mappingLen; ++i)
            {
                ulong key = reader.ReadUInt64();
                int l = reader.ReadInt32();
                byte[] buffer = new byte[l];
                reader.Read(buffer, 0, l);
                sceneMapping.Add(key, buffer);
            }
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            SceneSyncMessage t = new SceneSyncMessage(MessageTypes.SceneSyncMessage);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }

    public struct PlayerInfoSyncMessage : IMessage, ISerialized
    {
        public ushort Channel { get; set; }
        public ushort Part { get; set; }
        public ushort TotalParts { get; set; }
        public int Length { get; set; }
        public ushort ID { get; set; }
        public bool IsAsync { get; set; }

        public Dictionary<int, PlayerInformation> playerInfo;

        public PlayerInfoSyncMessage(ushort channel = MessageTypes.PlayerInfoSync)
        {
            Channel = channel;
            Part = 0;
            TotalParts = 1;
            Length = 0;
            ID = 0;
            IsAsync = false;
            playerInfo = new Dictionary<int, PlayerInformation>();
        }

        public void Serialize(ReusableStream writer)
        {
            writer.Write(playerInfo.Count);
            foreach (var mapping in playerInfo)
            {
                writer.Write(mapping.Key);
                writer.Write(mapping.Value.PlayerID);
                writer.Write(mapping.Value.PlayerName);
                writer.Write(mapping.Value.PlayerPing);
            }
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

            int mappingLen = reader.ReadInt32();

            playerInfo = new Dictionary<int, PlayerInformation>(mappingLen);
            for (int i = 0; i < mappingLen; ++i)
            {
                playerInfo.Add(reader.ReadInt32(), new PlayerInformation()
                {
                    PlayerID = reader.ReadInt32(),
                    PlayerName = reader.ReadString(),
                    PlayerPing = reader.ReadSingle(),
                });
            }
        }

        public T Deserialize<T>(ReusableStream reader) where T : IMessage
        {
            PlayerInfoSyncMessage t = new PlayerInfoSyncMessage(MessageTypes.PlayerInfoSync);
            t.Deserialize(reader.Data, (int)reader.Length);
            return (T)(t as IMessage);
        }
    }
}
