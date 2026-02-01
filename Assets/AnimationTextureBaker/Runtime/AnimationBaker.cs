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

        public void Scan()
        {
            var animation = GetComponent<Animation>();
            var animator = GetComponent<Animator>();

            if (animation != null)
            {
                settings.clips = new AnimationClip[animation.GetClipCount()];
                var i = 0;
                foreach (AnimationState state in animation)
                    settings.clips[i++] = state.clip;
            }
            else if (animator != null)
            {
                settings.clips = animator.runtimeAnimatorController.animationClips;
            }
        }

        public void Bake()
        {
#if UNITY_EDITOR
            // This will be called via the Editor View
            // We use a facade pattern or just bridge directly for now
#endif
        }
    }
}
