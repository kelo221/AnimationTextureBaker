#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Kelo.AnimationTextureBaker;

namespace Kelo.AnimationTextureBaker.Editor
{
    [CustomEditor(typeof(AnimationFramePlayer))]
    public class AnimationFramePlayerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var script = (AnimationFramePlayer)target;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Bone Attachments", EditorStyles.boldLabel);
            if (GUILayout.Button("Scan Bones", GUILayout.Width(100)))
            {
                script.ScanBones();
                EditorUtility.SetDirty(script);
            }
            EditorGUILayout.EndHorizontal();

            base.OnInspectorGUI();

            if (script.frameData == null) return;

            GUILayout.Space(10);
            
            // Note: currentPlayIndex and Play are now internal to the component
            // We can still expose the play buttons for convenience
            
            GUILayout.Label("Select animation to play: ");

            for (int i = 0; i < script.frameData.data.Length; i++)
            {
                var item = script.frameData.data[i];
                if (GUILayout.Button(item.name))
                {
                    script.Play(i);
                }
            }
        }
    }
}
#endif
