using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    public class AnimationCombinedFrames : ScriptableObject
    {
        [System.Serializable]
        public class FrameTimings
        {
            public string name;
            public int offset;
            public int frames;
            public float duration;
        }

        /// <summary>
        /// Per-frame bone transform data for attachments.
        /// </summary>
        [System.Serializable]
        public struct BoneFrameData
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        public FrameTimings[] data;
        
        /// <summary>
        /// Names of tracked bones (matches order of animatedBones in BakerSettings).
        /// </summary>
        public string[] boneNames;
        
        /// <summary>
        /// Whether each bone entry is a limb pair (true) or single bone (false).
        /// Limb pairs store midpoint position and axis-aligned rotation.
        /// </summary>
        public bool[] isLimbPair;
        
        /// <summary>
        /// Per-animation, per-frame, per-bone transform data.
        /// Outer array = animations (matches data[].name order).
        /// boneFrames[animIndex] contains flattened bone data: [frame0_bone0, frame0_bone1, ..., frame1_bone0, ...].
        /// </summary>
        public BoneFrameData[] boneFrames;
        
        /// <summary>
        /// Number of bones per frame.
        /// </summary>
        public int boneCount;
        
        /// <summary>
        /// Total number of frames across all animations.
        /// </summary>
        public int totalFrameCount;
        
        private System.Collections.Generic.Dictionary<string, int> _nameToIndex;

        /// <summary>
        /// Get the index of an animation by its name.
        /// </summary>
        public int GetIndexByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            if (_nameToIndex == null || _nameToIndex.Count != data.Length)
            {
                _nameToIndex = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] != null && !string.IsNullOrEmpty(data[i].name))
                    {
                        _nameToIndex[data[i].name] = i;
                    }
                }
            }

            if (_nameToIndex.TryGetValue(name, out int index))
            {
                return index;
            }

            return -1;
        }

        /// <summary>
        /// Get bone transform for a specific animation, frame, and bone index.
        /// </summary>
        public BoneFrameData GetBoneFrame(int animIndex, int frame, int boneIndex)
        {
            if (boneFrames == null || boneCount == 0 || animIndex < 0 || animIndex >= data.Length) return default;
            
            // Calculate starting frame offset for this animation
            int frameOffset = 0;
            for (int i = 0; i < animIndex; i++)
            {
                frameOffset += data[i].frames;
            }
            
            int index = (frameOffset + frame) * boneCount + boneIndex;
            if (index < 0 || index >= boneFrames.Length) return default;
            
            return boneFrames[index];
        }
    }
}
