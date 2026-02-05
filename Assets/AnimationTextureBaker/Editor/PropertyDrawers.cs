using UnityEditor;
using UnityEngine;

namespace Kelo.AnimationTextureBaker.Editor
{
    [CustomPropertyDrawer(typeof(ClipEntry))]
    public class ClipEntryDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Calculate column widths (Primary Clip | Secondary | Mask)
            float spacing = 4f;
            float totalWidth = position.width;
            float primaryWidth = (totalWidth - spacing * 2) * 0.45f;
            float secondaryWidth = (totalWidth - spacing * 2) * 0.35f;
            float maskWidth = (totalWidth - spacing * 2) * 0.2f;

            var primaryRect = new Rect(position.x, position.y, primaryWidth, position.height);
            var secondaryRect = new Rect(position.x + primaryWidth + spacing, position.y, secondaryWidth, position.height);
            var maskRect = new Rect(position.x + primaryWidth + secondaryWidth + spacing * 2, position.y, maskWidth, position.height);

            var primaryProp = property.FindPropertyRelative("primary");
            var secondaryProp = property.FindPropertyRelative("secondary");
            var maskProp = property.FindPropertyRelative("mask");

            EditorGUI.PropertyField(primaryRect, primaryProp, GUIContent.none);
            EditorGUI.PropertyField(secondaryRect, secondaryProp, GUIContent.none);
            EditorGUI.PropertyField(maskRect, maskProp, GUIContent.none);

            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(AnimatedBoneEntry))]
    public class AnimatedBoneEntryDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + 8; // Single row with some padding
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Add top padding for spacing between elements
            position.y += 4;
            position.height = EditorGUIUtility.singleLineHeight;

            float labelWidth = 45f;
            float spacing = 10f;
            float halfWidth = (position.width - labelWidth * 2 - spacing) / 2f;

            var boneAProp = property.FindPropertyRelative("boneA");
            var boneBProp = property.FindPropertyRelative("boneB");

            // Bone A: Label + Field
            var boneALabelRect = new Rect(position.x, position.y, labelWidth, position.height);
            var boneAFieldRect = new Rect(position.x + labelWidth, position.y, halfWidth, position.height);
            
            // Bone B: Label + Field
            var boneBLabelRect = new Rect(position.x + labelWidth + halfWidth + spacing, position.y, labelWidth, position.height);
            var boneBFieldRect = new Rect(position.x + labelWidth * 2 + halfWidth + spacing, position.y, halfWidth, position.height);

            EditorGUI.LabelField(boneALabelRect, "Bone A");
            EditorGUI.PropertyField(boneAFieldRect, boneAProp, GUIContent.none);
            EditorGUI.LabelField(boneBLabelRect, "Bone B");
            EditorGUI.PropertyField(boneBFieldRect, boneBProp, GUIContent.none);

            EditorGUI.EndProperty();
        }
    }
}
