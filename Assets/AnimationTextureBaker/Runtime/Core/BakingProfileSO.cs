using UnityEngine;
using System.Collections.Generic;

namespace Kelo.AnimationTextureBaker
{
    [System.Serializable]
    public class BoneMapping
    {
        public string label;
        public string boneAName;
        public string boneBName;
    }

    [CreateAssetMenu(fileName = "New Baking Profile", menuName = "Animation Texture Baker/Baking Profile")]
    public class BakingProfileSO : ScriptableObject
    {
        [Header("Bone Mapping Configuration")]
        [Tooltip("Define the bone pairs to scan for. Bone A is primary, Bone B is optional (midpoint/rotation).")]
        public List<BoneMapping> mappings = new List<BoneMapping>();

        [Header("Default Settings")]
        public int defaultFrameRate = 60;
        public bool bakeVelocity = false;
        public bool combineTextures = true;

        public void ApplyTo(AnimationBaker baker)
        {
            baker.settings.frameRate = defaultFrameRate;
            baker.settings.bakeVelocity = bakeVelocity;
            baker.settings.combineTextures = combineTextures;
        }
    }
}
