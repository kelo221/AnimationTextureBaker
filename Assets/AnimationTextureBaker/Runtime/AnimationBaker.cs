using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    public class AnimationBaker : MonoBehaviour
    {
        [Tooltip("Configuration for the animation texture baking process.")]
        public BakerSettings settings = new BakerSettings();


        /// <summary>
        /// Called when the component is first added or reset. Auto-populates default shader references.
        /// </summary>
        private void Reset()
        {
#if UNITY_EDITOR
            // Try to find default shaders if not already assigned
            if (settings.infoTexGen == null)
            {
                settings.infoTexGen = FindAsset<ComputeShader>("MeshInfoTextureGen");
            }
            
            if (settings.playShader == null)
            {
                settings.playShader = Shader.Find("AnimationBaker/VAT_MotionVectors");
            }
#endif
        }

#if UNITY_EDITOR
        private static T FindAsset<T>(string name) where T : UnityEngine.Object
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}");
            if (guids.Length > 0)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }
#endif

        [Header("SOLID Configuration")]
        [Tooltip("The profile defining character-specific settings and bone mappings.")]
        public BakingProfileSO profile;

        public void Scan()
        {
            var animation = GetComponent<Animation>();
            var animator = GetComponent<Animator>();

            AnimationClip[] sourceClips = null;
            
            if (animation != null)
            {
                sourceClips = new AnimationClip[animation.GetClipCount()];
                var i = 0;
                foreach (AnimationState state in animation)
                    sourceClips[i++] = state.clip;
            }
            else if (animator != null && animator.runtimeAnimatorController != null)
            {
                sourceClips = animator.runtimeAnimatorController.animationClips;
            }
            
            if (sourceClips != null)
            {
                settings.clips = new ClipEntry[sourceClips.Length];
                for (int i = 0; i < sourceClips.Length; i++)
                {
                    settings.clips[i] = new ClipEntry { primary = sourceClips[i] };
                }
            }

            if (profile != null)
            {
                profile.ApplyTo(this);
            }
        }

        public void Bake()
        {
#if UNITY_EDITOR
            // This will be called via the Editor View
            // We use a facade pattern or just bridge directly for now
#endif
        }

        public void ScanBones()
        {
            if (profile == null)
            {
                Debug.LogWarning("No Baking Profile assigned! Cannot scan bones.");
                return;
            }

            Transform[] allTransforms = GetComponentsInChildren<Transform>();
            
            settings.animatedBones = new AnimatedBoneEntry[profile.mappings.Count];
            for(int i = 0; i < profile.mappings.Count; i++)
            {
                var mapping = profile.mappings[i];
                settings.animatedBones[i] = new AnimatedBoneEntry();
                settings.animatedBones[i].boneA = FindInArray(allTransforms, mapping.boneAName);
                
                if(!string.IsNullOrEmpty(mapping.boneBName))
                    settings.animatedBones[i].boneB = FindInArray(allTransforms, mapping.boneBName);
            }
        }

        private Transform FindInArray(Transform[] array, string name)
        {
            foreach(var t in array) if(t.name == name) return t;
            return null;
        }

        public void SaveBones()
        {
#if UNITY_EDITOR
            if (profile == null)
            {
                Debug.LogWarning("No Baking Profile assigned! Cannot save bones.");
                return;
            }

            profile.mappings.Clear();
            foreach (var entry in settings.animatedBones)
            {
                if (entry == null || entry.boneA == null) continue;

                profile.mappings.Add(new BoneMapping
                {
                    label = entry.boneA.name,
                    boneAName = entry.boneA.name,
                    boneBName = entry.boneB != null ? entry.boneB.name : ""
                });
            }

            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"Saved {profile.mappings.Count} bone mappings to {profile.name}");
#endif
        }
    }
}
