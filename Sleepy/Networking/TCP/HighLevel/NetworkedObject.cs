using Sleepy.Streams;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sleepy.Net.TCP
{
    public class NetworkedObject : MonoBehaviour
    {
        [Flags]
        public enum UnityComponents
        {
            None = 0b0000_0000,
            Transform = 0b0000_0001,
            Rigidbody = 0b0000_0010,


            All = 0b0000_0011,
        }

        public UnityComponents NetworkedUnityComponents = UnityComponents.All;

        [HideInInspector]
        public ulong SceneID;
        public bool IsSceneObj => SceneID > 0;

        [HideInInspector]
        public ulong ID;
        [HideInInspector]
        public int Owner;
        [HideInInspector]
        public bool Init;

        void OnValidate()
        {
            if (Application.isPlaying || gameObject.scene.name == null) return;

            if (FindObjectOfType<NetworkedScene>() == null)
            {
                GameObject go = new GameObject("[NetworkedScene]");
                go.AddComponent<NetworkedScene>();
                go.transform.SetAsFirstSibling();
            }

            if (SceneID == 0) SceneID = FindObjectOfType<NetworkedScene>().NextID;
        }

        public INetworkable[] Components;

        public void Setup()
        {
            Init = true;
            RefreshComponents();
        }

        public void RefreshComponents()
        {
            Components = GetComponents<INetworkable>();
            for (int i = 0; i < Components.Length; ++i)
            {
                if (!Components[i].Init)
                {
                    Components[i].Init = true;
                    Components[i].ID = NetworkManager.Instance.NextNetworkObjectID;
                    Components[i].ThisObject = this;
                }
            }
        }

        // ======================= Data Flow ==========================

        public void GetData(ref FullSyncMessage fullMessage, bool isServer = false)
        {
            if (!Init) return;
            if (!isServer && Owner != NetworkManager.Instance.Owner) return;

            using (ReusableStream stream = new ReusableStream(64, true))
            {
                for (int i = 0; i < Components.Length; ++i)
                {
                    if (!Components[i].Init) continue;
                    stream.ResetForWriting();
                    Components[i].GetData(stream);
                    fullMessage.networkData.Data.Add(Components[i].ID, stream.Data.SubArray(0, (int)stream.Length));
                }
                stream.ResetForWriting();
                GetSelfData(stream);
                fullMessage.networkData.Data.Add(ID, stream.Data.SubArray(0, (int)stream.Length));
            }
        }

        public void RecvData(ref FullSyncMessage data)
        {
            if (!Init) return;
            byte[] d;

            if (Owner == NetworkManager.Instance.Owner && !data.force) return;

            using (ReusableStream stream = new ReusableStream(64, true))
            {
                for (int i = 0; i < Components.Length; ++i)
                {
                    if (data.networkData.Data.TryGetValue(Components[i].ID, out d))
                    {
                        if (!Components[i].Init) continue;

                        stream.ReplaceData(d, 0, d.Length, false);
                        Components[i].RecvData(stream);
                    }
                }

                if (data.networkData.Data.TryGetValue(ID, out d))
                {
                    stream.ReplaceData(d, 0, d.Length, false);
                    ParseSelfData(stream);
                }
            }
        }

        // ======================== Default Unity Components ==========================

        void GetSelfData(ReusableStream writer)
        {
            writer.Write((int)NetworkedUnityComponents);

            if (NetworkedUnityComponents.HasFlag(UnityComponents.Transform))
            {
                writer.Write(transform.position.x);
                writer.Write(transform.position.y);
                writer.Write(transform.position.z);

                writer.Write(transform.rotation.x);
                writer.Write(transform.rotation.y);
                writer.Write(transform.rotation.z);
                writer.Write(transform.rotation.w);

                writer.Write(transform.localScale.x);
                writer.Write(transform.localScale.y);
                writer.Write(transform.localScale.z);
            }

            if (NetworkedUnityComponents.HasFlag(UnityComponents.Rigidbody))
            {

            }
        }

        void ParseSelfData(ReusableStream reader)
        {
            UnityComponents networkedComponenets = (UnityComponents)reader.ReadInt32();

            if (networkedComponenets.HasFlag(UnityComponents.Transform))
            {
                transform.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                transform.rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                transform.localScale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            if (networkedComponenets.HasFlag(UnityComponents.Rigidbody))
            {

            }
        }

        // ======================== Get Network Instance Data =============================

        public byte[] GetInstanceInfo()
        {
            ReusableStream writer = new ReusableStream(128, true);

            writer.Write((int)NetworkedUnityComponents);
            writer.Write(ID);
            writer.Write(Owner);

            writer.Write(Components.Length);
            for (int i = 0; i < Components.Length; ++i)
            {
                writer.Write(Components[i].GetType().FullName);
                writer.Write(Components[i].ID);
            }

            return writer.Data.SubArray(0, (int)writer.Length);
        }

        public void SetInstanceInfo(byte[] data)
        {
            ReusableStream reader = new ReusableStream(data, 0, data.Length, false);

            NetworkedUnityComponents = (UnityComponents)reader.ReadInt32();
            ID = reader.ReadUInt64();
            Owner = reader.ReadInt32();
            Init = true;

            int componentsLen = reader.ReadInt32();

            Components = GetComponents<INetworkable>();
            for (int i = 0; i < componentsLen; ++i)
            {
                string fullClassName = reader.ReadString();
                ulong id = reader.ReadUInt64();
                foreach(INetworkable netObj in Components)
                {
                    if (!netObj.Init && netObj.GetType().FullName == fullClassName)
                    {
                        netObj.Init = true;
                        netObj.ID = id;
                        netObj.ThisObject = this;
                    }
                }
            }
        }
    }

    public abstract class NetworkedBehaviour : MonoBehaviour, INetworkable
    {
        public bool Init { get; set; }
        public ulong ID { get; set; }
        public NetworkedObject ThisObject { get; set; }
        public int Owner => ThisObject.Owner;

        public virtual void GetData(ReusableStream writer)
        {
        }

        public virtual void RecvData(ReusableStream reader)
        {
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(NetworkedObject), true)]
    public class NetworkedObjectInspector : UnityEditor.Editor
    {
        NetworkedObject script;
        private void OnEnable()
        {
            script = (NetworkedObject)target;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Network Info", EditorStyles.boldLabel);
            if (script.IsSceneObj)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"SceneID: {script.SceneID}");
                EditorGUILayout.EndHorizontal();
            }
            if (Application.isPlaying) EditorGUILayout.LabelField($"Object ID: {script.ID} | Owner: {script.Owner}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            base.OnInspectorGUI();
        }
    }

    [CustomEditor(typeof(NetworkedBehaviour), true)]
    public class NetworkedBehaviourInspector : UnityEditor.Editor
    {
        NetworkedBehaviour script;
        private void OnEnable()
        {
            script = (NetworkedBehaviour)target;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Network Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Object ID: {script.ID} | Owner: {(script.ThisObject != null ? script.Owner : -1)}");
        }
    }

#endif
}