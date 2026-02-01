using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    [ExecuteAlways]
    public class AnimationFramePlayer : MonoBehaviour
    {
        public AnimationCombinedFrames frameData;
        public int defaultAnimationIndex = 0;
        public bool playOnDemand = true;

        private MeshRenderer meshRenderer;
        private Material material;
        private int currentPlayIndex = -1;
        private bool isInitialized = false;
        private float timer = 0f;
        private float currentAnimDuration = 1f;

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (playOnDemand && isInitialized && material != null && currentPlayIndex >= 0)
            {
                // In Editor, outside of Play mode, we can use EditorApplication.timeSinceStartup if desired
                // but Time.deltaTime works if [ExecuteAlways] is active and something is dirtying the scene
                timer += Application.isPlaying ? Time.deltaTime : 0.016f; // Fallback for editor delta

                // Loop timer based on duration
                if (timer > currentAnimDuration) timer %= currentAnimDuration;
                
                material.SetFloat("_Timer", timer);
            }
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                // Play default animation on start at runtime
                if (isInitialized && frameData != null && frameData.data.Length > 0)
                {
                    Play(defaultAnimationIndex);
                }
            }
        }

        private void Initialize()
        {
            if (isInitialized && material != null) return;
            
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    material = meshRenderer.sharedMaterial;
                }
                else
                {
                    // Create instance at runtime
                    if (material == null || material == meshRenderer.sharedMaterial)
                        material = meshRenderer.material; 
                }
#else
                material = meshRenderer.material; 
#endif
                isInitialized = material != null;
            }
        }

        private void OnValidate()
        {
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer != null)
            {
                material = meshRenderer.sharedMaterial;
                if (frameData != null && frameData.data.Length > 0)
                {
                    // Update preview in editor
                    Play(Mathf.Clamp(defaultAnimationIndex, 0, frameData.data.Length - 1));
                }
            }
        }

        public void Play(int index)
        {
            Initialize(); 
            if (material == null) return;
            if (currentPlayIndex == index && !playOnDemand) return;

            currentPlayIndex = index;
            timer = 0f; // Reset timer for on-demand playback

            if (currentPlayIndex < 0 || frameData == null || currentPlayIndex >= frameData.data.Length)
            {
                UpdateMaterial();
            }
            else
            {
                var item = frameData.data[currentPlayIndex];
                currentAnimDuration = item.duration;
                UpdateMaterial(item.frames, item.offset, item.duration);
            }
        }

        private void UpdateMaterial(int frames = 0, int offset = 0, float duration = 0)
        {
            if (material == null) return;

            if (playOnDemand)
            {
                material.EnableKeyword("_TIMERMODE_SCRIPT");
                material.DisableKeyword("_TIMERMODE_SHADER");
            }
            else
            {
                material.DisableKeyword("_TIMERMODE_SCRIPT");
                material.EnableKeyword("_TIMERMODE_SHADER");
            }

            material.SetFloat("_Timer", 0);

            // Vector property for compatible shaders
            material.SetVector("_AnimDurationAndOffset", new Vector4(duration, offset, 0, 0));

            // Individual properties for AnimationVertexCombined
            material.SetFloat("_Frames", frames);
            material.SetFloat("_Offset", offset);
            material.SetFloat("_Duration", duration);
        }
    }
}
