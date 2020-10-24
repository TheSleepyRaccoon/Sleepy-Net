using Sleepy.Attributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sleepy.Net.TCP
{
    public class NetworkedScene : MonoBehaviour
    {
        public ulong _counter = 1;
        public ulong NextID => _counter++;

        public bool IsInit;
        public NetworkedObject[] SceneNetworkedObjects;
        public void Start()
        {
            Init();
        }

        public void Init()
        {
            if (IsInit) return;
            IsInit = true;

            // NOTE: Expensive
            SceneNetworkedObjects = FindObjectsOfType<NetworkedObject>().Where(x => x.gameObject.scene.name == gameObject.scene.name && x.SceneID != 0).ToArray();
        }

#if UNITY_EDITOR
        public void FullReset()
        {
            NetworkedObject[] netowrkedObjects = FindObjectsOfType<NetworkedObject>();
            _counter = 1;
            for (int i = 0; i < netowrkedObjects.Length; ++i) netowrkedObjects[i].SceneID = NextID;
        }
#endif
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(NetworkedScene), true)]
    public class NetworkedSceneInspector : Editor
    {
        NetworkedScene script;
        private void OnEnable()
        {
            script = (NetworkedScene)target;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Networked Scene Info", EditorStyles.boldLabel);
            if (GUILayout.Button("Reset All", GUILayout.Width(100)))
            {
                script.FullReset();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Current Counter: {script._counter}");
            EditorGUILayout.EndVertical();
            

        }
    }
#endif
}
