#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Sleepy.Net.TCP
{
    [CustomEditor(typeof(NetworkManager), true)]
    public class NetworkManagerEditor : Editor
    {
        NetworkManager script;

        private void OnEnable()
        {
            script = (NetworkManager)target;
        }


        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();

            if (Application.isPlaying)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Network Info", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                if (script.type.HasFlag(NetworkManager.Type.Server))
                {
                    EditorGUILayout.LabelField($"Server Active | Players: {script.PlayersIDs.Count}");
                    foreach(var pair in script.PlayersIDs)
                    {
                        EditorGUILayout.LabelField($"    Player {pair.Value} [{script.PlayerInfo[pair.Value]}] -> {pair.Key}");
                    }
                }
                EditorGUILayout.Space();
                if (script.type.HasFlag(NetworkManager.Type.Client))
                {
                    EditorGUILayout.LabelField($"Client Active");
                    EditorGUILayout.LabelField($"    Connected To: {script.IP}:{script.Port}");
                    EditorGUILayout.LabelField($"    Owner: {script.Owner}");
                }
                EditorGUILayout.EndVertical();
            }


            EditorGUILayout.LabelField("Connection Info", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("IP:Port");
            script.IP = EditorGUILayout.TextField(script.IP);
            script.Port = (ushort)EditorGUILayout.IntField(script.Port);
            EditorGUILayout.EndHorizontal();
            script.UpdateFrequency = EditorGUILayout.IntSlider("Update Frequency", script.UpdateFrequency, 6, 60);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            script.InitOnStart = EditorGUILayout.Toggle("Init On Start", script.InitOnStart);
            script.ShowDebugMenu = EditorGUILayout.Toggle("Show Debug Menu", script.ShowDebugMenu);
            script.OnScreenDebugInfo = EditorGUILayout.Toggle("OnScreen Debug Info", script.OnScreenDebugInfo);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(target);
            }
        }
    }

}
#endif