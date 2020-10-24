using Open.Nat;
using Sleepy.Collections;
using Sleepy.Streams;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sleepy.Net.TCP
{
    public class NetworkManager : Types.Singleton<NetworkManager>
    {
        protected static Log Log = new Log("NetworkManager", "lime", true);

        public Client client;
        public Server server;

        [Flags]
        public enum Type
        {
            None = 0b_0000_0000,
            Server = 0b_0000_0001,
            Client = 0b_0000_0010,
            Both = Server | Client
        }
        public Type type;

        public int UpdateFrequency = 30;
        public bool InitOnStart;

        public string IP;
        public ushort Port;

        [HideInInspector]
        public bool IsNatSetup;

        public int Owner;
        public string PlayerName;

        public Dictionary<Connection, int> PlayersIDs = new Dictionary<Connection, int>();
        public Dictionary<int, PlayerInformation> PlayerInfo = new Dictionary<int, PlayerInformation>(); // Client should also know this
        public Dictionary<ulong, NetworkedObject> NetworkedObjects = new Dictionary<ulong, NetworkedObject>();
        public Dictionary<string, NetworkedScene> NetworkedScenes = new Dictionary<string, NetworkedScene>();

        public Dictionary<Connection, ulong> LatestPacketFromConnection = new Dictionary<Connection, ulong>();
        public ulong LatestPacketFromServer;

        public Dictionary<int, List<CreateObjectResponseMessage>> CachedCreatedObjects = new Dictionary<int, List<CreateObjectResponseMessage>>();
        public void AddCachedObject(int player, ref CreateObjectResponseMessage message)
        {
            if (CachedCreatedObjects.TryGetValue(player, out var list)) list.Add(message);
            else CachedCreatedObjects[player] = new List<CreateObjectResponseMessage>() { message };
        }

        public Dictionary<string, SceneSyncMessage> KnownSceneSyncData = new Dictionary<string, SceneSyncMessage>();

        // ==================== Setup =====================

        protected void Awake()
        {
            OnAwake(this);
        }

        protected async void Start()
        {
            DontDestroyOnLoad(this);

            await SetupNat();

            if (InitOnStart) Setup();

            StartCoroutine(InternalUpdate());
        }

        protected async void OnDestroy()
        {
            StopClient();
            StopServer();


            if (IsNatSetup)
            {
                await ClearNat();
            }
        }

        public void Setup()
        {
            if (type.HasFlag(Type.Server))
            {
                SetupServer(Port);
            }

            if (type.HasFlag(Type.Client))
            {
                SetupClient(IP, Port);
            }
        }

        public void SetupClient(string ip, ushort port)
        {
            Log.Write("Starting Client");
            IP = type.HasFlag(Type.Server) ? "127.0.0.1" : ip;
            Port = port;

            client = new Client(IP, Port);

            BindClientCallbacks();

            client.Connect();
            type |= Type.Client;
            if (client.Connected) Log.Write("Client Started");
        }

        public void SetupServer(ushort port)
        {
            Log.Write("Starting Server");
            Port = port;

            server = new Server(Port);            

            BindServerCallbacks();

            server.Start();
            type |= Type.Server;
            if (server.Active) Log.Write("Server Started");
        }

        public virtual void BindClientCallbacks()
        {
            client.OnConnect += OnClientConnect;
            client.OnDisconnect += OnClientDisconnect;

            client.Bind<PlayerInfoSyncMessage>(MessageTypes.PlayerInfoSync, ClientRecv_PlayerInfoSyncResponse);
            client.Bind<FullSyncMessage>(MessageTypes.FullServerMessage, ClientRecv_FullMessage);
            client.Bind<ClientRegisterResponseMessage>(MessageTypes.ClientRegistrationMessage, ClientRecv_RegistrationResponse);
            client.Bind<CreateObjectResponseMessage>(MessageTypes.CreateObjectMessage, ClientRecv_CreateObjectResponse);
            client.Bind<DestroyObjectsResponseMessage>(MessageTypes.DestroyObjectMessage, ClientRecv_DestroyObjectResponse);
            client.Bind<LoadSceneMessage>(MessageTypes.LoadSceneMessage, ClientRecv_LoadSceneResponse);
            client.Bind<SceneSyncMessage>(MessageTypes.SceneSyncMessage, ClientRecv_SceneSyncResponse);
        }

        public virtual void BindServerCallbacks()
        {
            server.OnConnect += OnServerConnect;
            server.OnDisconnect += OnServerDisconnect;

            server.Bind<PlayerInfoSyncMessage>(MessageTypes.PlayerInfoSync, ServerRecv_PlayerInfoSyncRequest);
            server.Bind<FullSyncMessage>(MessageTypes.FullClientMessage, ServerRecv_FullMessage);
            server.Bind<ClientRegisterRequestMessage>(MessageTypes.ClientRegistrationMessage, ServerRecv_RegistrationRequest);
            server.Bind<CreateObjectRequestMessage>(MessageTypes.CreateObjectMessage, ServerRecv_CreateObjectRequest);
            server.Bind<DestroyObjectsRequestMessage>(MessageTypes.DestroyObjectMessage, ServerRecv_DestroyObjectRequest);
            server.Bind<GetCachedObjectsRequestMessage>(MessageTypes.GetCachedObjectsMessage, ServerRecv_GetCachedObjectsRequest);
            server.Bind<LoadSceneMessage>(MessageTypes.LoadSceneMessage, ServerRecv_LoadSceneRequest);
            server.Bind<SceneSyncMessage>(MessageTypes.SceneSyncMessage, ServerRecv_SceneSyncRequest);
            server.Bind<LoadSceneCompleteMessage>(MessageTypes.LoadSceneCompleteMessage, ServerRecv_LoadSceneCompleteMessage);
        }

        public void StopClient()
        {
            if (type.HasFlag(Type.Client))
            {
                if (client != null)
                {
                    client.Disconnect();
                    client = null;
                }

                type &= ~Type.Client;
            }

            if (type == Type.None)
            {
                ClearInfo();
            }
        }

        public void StopServer()
        {
            if (type.HasFlag(Type.Server))
            {
                if (server != null)
                {
                    server.Stop();
                    server = null;
                }
                type &= ~Type.Server;
            }

            if (type == Type.None)
            {
                ClearInfo();
            }

        }

        public void ClearInfo()
        {
            PlayersIDs.Clear();
            PlayerInfo.Clear();
            NetworkedObjects.Clear();
            NetworkedScenes.Clear();
            LatestPacketFromConnection.Clear();
            CachedCreatedObjects.Clear();
            KnownSceneSyncData.Clear();
            ScenesLoading.Clear();
            LatestPacketFromServer = 0;
            Owner = 0;
        }

        // ====================== NAT ========================

        protected Mapping mapping;
        protected NatDevice device;
        protected NatDiscoverer nat = new NatDiscoverer();

        protected async Task SetupNat()
        {
            Log.Write("Setting Up Nat");
            mapping = new Mapping(Protocol.Tcp, Port, Port, int.MaxValue, "Library");

            CancellationTokenSource cts = new CancellationTokenSource(5000);
            device = await nat.DiscoverDeviceAsync(PortMapper.Upnp, cts);

            IEnumerable<Mapping> mappings = await device.GetAllMappingsAsync();

            if (!mappings.Contains(mapping))
            {
                Log.Write("Adding Mapping");
                await device.CreatePortMapAsync(mapping);
            }

            IsNatSetup = true;
            Log.Write("Nat Setup");
        }

        protected async Task ClearNat()
        {
            Log.Write("Clearing Nat");

            if (device != null)
            {
                IEnumerable<Mapping> mappings = await device.GetAllMappingsAsync();

                if (mappings.Contains(mapping))
                {
                    Log.Write("Removing Mapping");
                    await device.DeletePortMapAsync(mapping);
                }
            }

            IsNatSetup = false;
            Log.Write("Nat Cleared");
        }

        // ====================== Registration ========================

        protected ulong currentNetworkObjectCounter;
        public ulong NextNetworkObjectID => currentNetworkObjectCounter++;

        protected int currentPlayerIDCounter;
        public int NextPlayerID => currentPlayerIDCounter++;

        protected ulong clientMessageCount;
        protected ulong serverMessageCount;

        public void RegisterObjects()
        {
            NetworkedObject[] objs = FindObjectsOfType<NetworkedObject>();
            for (int i = 0; i < objs.Length; ++i)
            {
                objs[i].Setup();
                objs[i].ID = NextNetworkObjectID;
                NetworkedObjects.Add(objs[i].ID, objs[i]);
            }
        }

        // ===================== Updates ==========================

        public virtual void Update()
        {
            if (type.HasFlag(Type.Client) && client != null)
            {
                client.Update();
            }

            if (type.HasFlag(Type.Server) && server != null)
            {
                server.Update();
            }

            if (_ActionsToDoOnMainThread.Count > 0)
            {
                if (_ActionsToDoOnMainThread.TryDequeueAll(out Action[] actions))
                {
                    for(int i = 0; i < actions.Length; ++i)
                    {
                        actions[i]?.Invoke();
                    }
                }
            }
        }

        protected IEnumerator InternalUpdate()
        {
            while (true)
            {
                if (type.HasFlag(Type.Client) && client != null && client.Connected && !type.HasFlag(Type.Server))
                {
                    try
                    {
                        FullSyncMessage fullMessage = new FullSyncMessage(MessageTypes.FullClientMessage) { messageNum = clientMessageCount++ };
                        foreach (var obj in NetworkedObjects)
                        {
                            obj.Value.GetData(ref fullMessage, false);
                        }
                        client.Send(ref fullMessage);

                        if (clientMessageCount % 30 == 0)
                        {
                            client.Send(new PlayerInfoSyncMessage(MessageTypes.PlayerInfoSync));
                        }
                    }
                    catch(Exception e)
                    {
                        Log.Write($"Error With Sending Client Message | {e.Message}\n{e.StackTrace}");
                    }
                }

                if (type.HasFlag(Type.Server) && server != null && server.Active && server.Connections.Count() > 0)
                {
                    try
                    {
                        FullSyncMessage fullMessage = new FullSyncMessage(MessageTypes.FullServerMessage) { messageNum = serverMessageCount++ };
                        foreach (var obj in NetworkedObjects)
                        {
                            obj.Value.GetData(ref fullMessage, true);
                        }

                        if (type.HasFlag(Type.Client))
                        {
                            server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref fullMessage); // FIXME: Nicer way of doing this, probably store it
                        }
                        else
                        {
                            server.SendMulti(server.Connections.Values.ToArray(), ref fullMessage);
                        }

                        foreach(var pair in PlayersIDs)
                        {
                            PlayerInfo[pair.Value].PlayerPing = pair.Key.FTT;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Write($"Error With Sending Server Message | {e.Message}\n{e.StackTrace}");
                    }

                }

                yield return new WaitForSecondsRealtime(1f / UpdateFrequency);
            }

        }

        // ===================== Helpers ======================

        class SceneLoadingProgress
        {
            public string sceneName;
            public List<int> playersLoaded;
            public Action Callback;
        }
        Dictionary<string, SceneLoadingProgress> ScenesLoading = new Dictionary<string, SceneLoadingProgress>();

        public void LoadScene(string sceneName, Action callback = null)
        {
            // FIXME: Maybe the server is completely seperate
            if (type.HasFlag(Type.Server) && type.HasFlag(Type.Client))
            {
                LoadSceneMessage loadSceneMessage = new LoadSceneMessage(MessageTypes.LoadSceneMessage)
                {
                    SceneName = sceneName
                };

                ScenesLoading[sceneName] = new SceneLoadingProgress() { sceneName = sceneName, playersLoaded = new List<int>(), Callback = callback };
                client.Send(ref loadSceneMessage);
            }
        }

        SafeQueue<Action> _ActionsToDoOnMainThread = new SafeQueue<Action>();
        public void DoOnMainThread(Action a) => _ActionsToDoOnMainThread.Enqueue(a);

        // ===================== Messages ==========================

        public virtual void OnClientConnect()
        {
            Log.Write("Client | Connected");

            ClientRegisterRequestMessage req = new ClientRegisterRequestMessage(MessageTypes.ClientRegistrationMessage)
            {
                playerName = PlayerName,
            };
            client.Send(ref req);
        }

        public virtual void OnClientDisconnect()
        {
            Log.Write("Client | Disconnected");
            StopClient();
        }

        public virtual void OnServerConnect(Connection conn)
        {
            Log.Write($"Server | {conn} Connected");
        }

        public virtual void OnServerDisconnect(Connection conn)
        {
            Log.Write($"Server | {conn} Disconnected");
            if (server == null) return; // server is shutting down and has already told players to leave

            if (PlayersIDs.TryGetValue(conn, out int playerID))
            {
                PlayersIDs.Remove(conn);

                if (PlayerInfo.ContainsKey(playerID))
                {
                    PlayerInfo.Remove(playerID);
                }

                if (CachedCreatedObjects.ContainsKey(playerID))
                {
                    CachedCreatedObjects.Remove(playerID);
                }

                DestroyObjectsResponseMessage destMes = new DestroyObjectsResponseMessage(MessageTypes.DestroyObjectMessage);
                destMes.networkIDs = NetworkedObjects.Where(x => x.Value.Owner == playerID).Select(x => x.Value.ID).ToList();

                if (server == null) return; // server is shutting down and has already told players to leave
                server.SendMulti(PlayersIDs.Where(x => x.Value != playerID).Select(x => x.Key).ToArray(), ref destMes);
            }

            PlayerInfoSyncMessage resp = new PlayerInfoSyncMessage(MessageTypes.PlayerInfoSync)
            {
                playerInfo = PlayerInfo
            };

            if (type.HasFlag(Type.Client))
            {
                if (server == null) return; // server is shutting down and has already told players to leave
                server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref resp);
            }
            else
            {
                if (server == null) return; // server is shutting down and has already told players to leave
                server.SendMulti(server.Connections.Values.ToArray(), ref resp);
            }
        }

        // From Server
        public virtual void ClientRecv_FullMessage(FullSyncMessage resp)
        {
            if (type.HasFlag(Type.Server)) return; // We are the server aswell, we will already be up to date, no point in resetting everything

            if (LatestPacketFromServer < resp.messageNum)
            {
                LatestPacketFromServer = resp.messageNum;
                foreach (var obj in NetworkedObjects) obj.Value.RecvData(ref resp);
            }
        }

        public virtual void ClientRecv_RegistrationResponse(ClientRegisterResponseMessage resp)
        {
            if (resp.Accepted)
            {
                Owner = resp.PlayerID;
                PlayerName = resp.playerName;
            }
        }

        public virtual void ClientRecv_CreateObjectResponse(CreateObjectResponseMessage resp)
        {
            switch (resp.spawnType)
            {
                case CreateObjectResponseMessage.SpawnType.Player:
                    CreatePlayerData data = new CreatePlayerData()
                    {
                        IsLocal = resp.owner == Owner
                    };
                    
                    Messaging.Messaging.Instance.Broadcast("Create Player",  data);
                    if (data.networkedObject != null)
                    {
                        data.networkedObject.SetInstanceInfo(resp.networkedObjectData);
                        NetworkedObjects.Add(data.networkedObject.ID, data.networkedObject);
                    }
                    break;
                case CreateObjectResponseMessage.SpawnType.Prefab:
                    GameObject go = Instantiate(Resources.Load<GameObject>(resp.prefabName));

                    NetworkedObject obj = go.GetComponent<NetworkedObject>();
                    if (obj == null) obj = go.AddComponent<NetworkedObject>();

                    obj.SetInstanceInfo(resp.networkedObjectData);
                    NetworkedObjects.Add(obj.ID, obj);
                    break;
            }
        }

        public virtual void ClientRecv_DestroyObjectResponse(DestroyObjectsResponseMessage resp)
        {
            foreach(ulong networkID in resp.networkIDs)
            {
                if (NetworkedObjects.TryGetValue(networkID, out NetworkedObject obj))
                {
                    Destroy(obj.gameObject);
                    NetworkedObjects.Remove(networkID);
                }
            }
        }

        public virtual void ClientRecv_LoadSceneResponse(LoadSceneMessage resp)
        {
            var loadOp = SceneManager.LoadSceneAsync(resp.SceneName, resp.Addative ? LoadSceneMode.Additive : LoadSceneMode.Single);
            loadOp.allowSceneActivation = false;
            StartCoroutine(ClientLoadSceneAndSync(loadOp, resp.SceneName));
        }

        public virtual void ClientRecv_SceneSyncResponse(SceneSyncMessage resp)
        {
            KnownSceneSyncData[resp.sceneName] = resp;
        }

        protected virtual IEnumerator ClientLoadSceneAndSync(AsyncOperation op, string sceneName)
        {
            yield return new WaitUntil(() => op.progress >= 0.9f);
            yield return new WaitUntil(() => KnownSceneSyncData.ContainsKey(sceneName));
            KnownSceneSyncData.TryGetValue(sceneName, out SceneSyncMessage resp);

            op.allowSceneActivation = true;
            yield return new WaitForEndOfFrame();

            NetworkedScene networkedScene = FindObjectsOfType<NetworkedScene>().FirstOrDefault(x => x.gameObject.scene.name == sceneName);
            
            if (networkedScene != null)
            {
                networkedScene.Init();
                for (int i = 0; i < networkedScene.SceneNetworkedObjects.Length; ++i)
                {
                    if (resp.sceneMapping.TryGetValue(networkedScene.SceneNetworkedObjects[i].ID, out byte[] data))
                    {
                        networkedScene.SceneNetworkedObjects[i].SetInstanceInfo(data);
                    }
                }
            }

            client.Send(new LoadSceneCompleteMessage(MessageTypes.LoadSceneCompleteMessage) { SceneName = sceneName });
        }
        
        public virtual void ClientRecv_PlayerInfoSyncResponse(PlayerInfoSyncMessage resp)
        {
            PlayerInfo = resp.playerInfo;
        }


        // From Clients
        public virtual void ServerRecv_FullMessage(Connection conn, FullSyncMessage req)
        {
            bool alreadyKnown = LatestPacketFromConnection.TryGetValue(conn, out ulong num);
            if ((alreadyKnown && num < req.messageNum) || !alreadyKnown)
            {
                LatestPacketFromConnection[conn] = req.messageNum;
                foreach (var obj in NetworkedObjects) obj.Value.RecvData(ref req);
            }
        }

        public virtual void ServerRecv_RegistrationRequest(Connection conn, ClientRegisterRequestMessage req)
        {
            ClientRegisterResponseMessage resp = new ClientRegisterResponseMessage(MessageTypes.ClientRegistrationMessage);

            if (!PlayersIDs.ContainsKey(conn))
            {
                resp.Accepted = true;
                resp.PlayerID = NextPlayerID;
                resp.playerName = req.playerName;

                PlayersIDs.Add(conn, resp.PlayerID);
                PlayerInfo.Add(resp.PlayerID, new TCP.PlayerInformation() { PlayerID = resp.PlayerID, PlayerName = resp.playerName });
            }
            else
            {
                resp.Accepted = false;
                resp.PlayerID = -1;
            }

            req.ReplyWith(ref resp);
            server.Send(conn, ref resp);

            PlayerInfoSyncMessage resp2 = new PlayerInfoSyncMessage(MessageTypes.PlayerInfoSync)
            {
                playerInfo = PlayerInfo
            };

            if (type.HasFlag(Type.Client))
            {
                server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref resp2);
            }
            else
            {
                server.SendMulti(server.Connections.Values.ToArray(), ref resp2);
            }
        }

        public virtual void ServerRecv_GetCachedObjectsRequest(Connection conn, GetCachedObjectsRequestMessage req)
        {
            foreach (var pair in CachedCreatedObjects)
            {
                if (pair.Key == req.owner) continue;

                for (int i = 0; i < pair.Value.Count; ++i)
                {
                    server.Send(conn, pair.Value[i]);
                }
            }
        }

        public virtual void ServerRecv_CreateObjectRequest(Connection conn, CreateObjectRequestMessage req)
        {
            CreateObjectResponseMessage resp = new CreateObjectResponseMessage(MessageTypes.CreateObjectMessage);

            // TODO: actually check this is valid to do 
            switch (req.spawnType)
            {
                case CreateObjectRequestMessage.SpawnType.Player:

                    CreatePlayerData data = new CreatePlayerData()
                    {
                        IsLocal = req.playerToOwn == Owner
                    };

                    Messaging.Messaging.Instance.Broadcast("Create Player", data);
                    if (data.networkedObject != null)
                    {
                        data.networkedObject.ID = NextNetworkObjectID;
                        data.networkedObject.Owner = req.playerToOwn;
                        data.networkedObject.Setup();
                        NetworkedObjects.Add(data.networkedObject.ID, data.networkedObject);

                        resp.owner = req.playerToOwn;
                        resp.networkedObjectData = data.networkedObject.GetInstanceInfo();
                        resp.prefabName = "";
                        resp.spawnType = CreateObjectResponseMessage.SpawnType.Player;
                    }
                    break;

                case CreateObjectRequestMessage.SpawnType.Prefab:
                    GameObject go = Instantiate(Resources.Load<GameObject>(req.prefabName));

                    NetworkedObject obj = go.GetComponent<NetworkedObject>();
                    if (obj == null) obj = go.AddComponent<NetworkedObject>();

                    obj.ID = NextNetworkObjectID;
                    obj.Owner = req.playerToOwn;
                    obj.Setup();

                    NetworkedObjects.Add(obj.ID, obj);

                    resp.owner = req.playerToOwn;
                    resp.networkedObjectData = obj.GetInstanceInfo();
                    resp.prefabName = req.prefabName;
                    resp.spawnType = CreateObjectResponseMessage.SpawnType.Prefab;
                    break;
            }

            req.ReplyWith(ref resp);

            if (type.HasFlag(Type.Client))
            {
                server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref resp);
            }
            else
            {
                server.SendMulti(server.Connections.Values.ToArray(), ref resp);
            }

            if (req.perminant) AddCachedObject(req.playerToOwn, ref resp);
        }

        public virtual void ServerRecv_DestroyObjectRequest(Connection conn, DestroyObjectsRequestMessage req)
        {
            if (NetworkedObjects.TryGetValue(req.networkID, out NetworkedObject obj))
            {
                if (obj.Owner == req.playerRequested)
                {
                    Destroy(obj.gameObject);
                    NetworkedObjects.Remove(req.networkID);

                    DestroyObjectsResponseMessage resp = new DestroyObjectsResponseMessage(MessageTypes.DestroyObjectMessage);
                    req.ReplyWith(ref resp);
                    resp.networkIDs = new List<ulong>() { req.networkID };

                    if (type.HasFlag(Type.Client))
                    {
                        server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref resp);
                    }
                    else
                    {
                        server.SendMulti(server.Connections.Values.ToArray(), ref resp);
                    }
                }
            }
        }

        public virtual void ServerRecv_LoadSceneRequest(Connection conn, LoadSceneMessage req)
        {
            if (PlayersIDs[conn] != 0) return; // We only want the host to 

            SceneManager.LoadSceneAsync(req.SceneName, req.Addative ? LoadSceneMode.Additive : LoadSceneMode.Single).completed += (a) => 
            {
                NetworkedScene networkedScene = FindObjectsOfType<NetworkedScene>().FirstOrDefault(x => x.gameObject.scene.name == req.SceneName);
                if (networkedScene != null)
                {
                    SceneSyncMessage syncMessage = new SceneSyncMessage(MessageTypes.SceneSyncMessage) { sceneName = req.SceneName };

                    networkedScene.Init();
                    for(int i = 0; i < networkedScene.SceneNetworkedObjects.Length; ++i)
                    {
                        networkedScene.SceneNetworkedObjects[i].Setup();
                        networkedScene.SceneNetworkedObjects[i].ID = NextNetworkObjectID;
                        networkedScene.SceneNetworkedObjects[i].Owner = -1; // server owns all scene objects atm
                        NetworkedObjects.Add(networkedScene.SceneNetworkedObjects[i].ID, networkedScene.SceneNetworkedObjects[i]);
                        syncMessage.sceneMapping.Add(networkedScene.SceneNetworkedObjects[i].SceneID, networkedScene.SceneNetworkedObjects[i].GetInstanceInfo());
                    }

                    if (type.HasFlag(Type.Client))
                    {
                        server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref syncMessage);
                        client.Send(new LoadSceneCompleteMessage(MessageTypes.LoadSceneCompleteMessage) { SceneName = req.SceneName });
                    }
                    else
                    {
                        server.SendMulti(server.Connections.Values.ToArray(), ref syncMessage);
                    }

                    KnownSceneSyncData[req.SceneName] = syncMessage;
                }
            };

            if (type.HasFlag(Type.Client))
            {
                server.SendMulti(PlayersIDs.Where(x => x.Value != 0).Select(x => x.Key).ToArray(), ref req);
            }
            else
            {
                server.SendMulti(server.Connections.Values.ToArray(), ref req);
            }
        }

        public virtual void ServerRecv_SceneSyncRequest(Connection conn, SceneSyncMessage req)
        {
            if (KnownSceneSyncData.TryGetValue(req.sceneName, out var value))
            {
                req.sceneMapping = value.sceneMapping;
                server.Send(conn, ref req);
            }
        }

        public virtual void ServerRecv_PlayerInfoSyncRequest(Connection conn, PlayerInfoSyncMessage req)
        {
            req.playerInfo = PlayerInfo;
            server.Send(conn, ref req);
        }        
        
        public virtual void ServerRecv_LoadSceneCompleteMessage(Connection conn, LoadSceneCompleteMessage req)
        {
            if (ScenesLoading.TryGetValue(req.SceneName, out var progress))
            {
                progress.playersLoaded.Add(PlayersIDs[conn]);
                if (progress.playersLoaded.Count == PlayersIDs.Count)
                {
                    // Loading Complete
                    progress.Callback?.Invoke();
                    ScenesLoading.Remove(req.SceneName);
                }
            }
        }


        // ===================== EditorUI =============================

        [Header("Debug")]
        public bool ShowDebugMenu;
        public bool OnScreenDebugInfo;

        protected static Rect RectSlot1 = new Rect(10, 0, Screen.width * 2, 30);
        protected static Rect RectSlot2 = new Rect(10, 60, 100, 30);
        protected static Rect RectSlot3 = new Rect(115, 60, 100, 30);
        protected static Rect RectSlot4 = new Rect(10, 30, 205, 25);

        public void OnGUI()
        {
            string info = $"Current State: {type.ToString()}";

            if (type.HasFlag(Type.Server) && server != null)
            {
                if (ShowDebugMenu && GUI.Button(RectSlot2, "Unhost"))
                {
                    StopServer();
                }
                info += $" | Server: {server.Active} [{server.Connections.Count()}] - S:{Stats.BytesToString(server.stats.SentBytesLastSecond)}/s - R:{Stats.BytesToString(server.stats.RecvBytesLastSecond)}/s";
            }
            else
            {
                if (ShowDebugMenu && GUI.Button(RectSlot2, "Host"))
                {
                    SetupServer(Port);
                }
            }

            if (type.HasFlag(Type.Client) && client != null)
            {
                if (ShowDebugMenu && GUI.Button(RectSlot3, "Leave"))
                {
                    StopClient();
                }
                info += $" | Player ID: {Owner}";
                info += $" | Client: {(client.Connected ? "Connected" : "Disconnected")}  - S:{Stats.BytesToString(client.stats.SentBytesLastSecond)}/s - R:{Stats.BytesToString(client.stats.RecvBytesLastSecond)}/s - FTT: {client.FTT : 0.00}ms";
            }
            else
            {
                if (ShowDebugMenu && GUI.Button(RectSlot3, "Join"))
                {
                    SetupClient(IP, Port);
                }
            }


            if (OnScreenDebugInfo) GUI.Label(RectSlot1, info);
            if (ShowDebugMenu) IP = GUI.TextField(RectSlot4, IP);
        }
    }

    public class CreatePlayerData
    {
        // Paramiters
        public bool IsLocal;

        // Returned
        public NetworkedObject networkedObject;
    }

    public class PlayerInformation
    {
        public string PlayerName;
        public int PlayerID;
        public float PlayerPing;
    }
}