using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace Kelo.AnimationTextureBaker
{
    [ExecuteAlways]
    public class AnimationFramePlayer : MonoBehaviour
    {
        public AnimationCombinedFrames frameData;
        public int defaultAnimationIndex = 0;
        public bool playOnDemand = true;
        public bool randomStartOffset = true;
        
        [Header("Bone Attachments")]
        [Tooltip("Transforms to update with baked bone data. Order must match animatedBones in BakerSettings. For limb pairs, assign a single transform that will be positioned at the midpoint.")]
        public Transform[] animatedBones;

        private MeshRenderer meshRenderer;
        private Material material;
        private int currentPlayIndex = -1;
        private bool isInitialized = false;
        private float timer = 0f;
        private float currentAnimDuration = 1f;
        private int currentFrameCount = 1;
        
        // Blending state
        private int blendFromIndex = -1;
        private float blendFromTimer = 0f;
        private float blendDuration = 0f;
        private float blendElapsed = 0f;
        private bool isBlending = false;
        
        // Loop and finish state (for AI integration)
        private bool isLooping = true;
        private bool hasReportedFinish = false;
        
        // Job system bone tracking
        private TransformAccessArray m_BoneTransformAccess;
        private NativeArray<NativeBoneFrameData> m_NativeBoneFrames;
        private NativeArray<NativeAnimTiming> m_NativeAnimTimings;
        private bool m_BoneJobsInitialized;
        
        /// <summary>
        /// Event fired when a non-looping animation completes. Passes the animation index.
        /// </summary>
        public event Action<int> OnAnimationFinished;
        
        /// <summary>
        /// Current frame index within the animation (0 to MaxFrame-1).
        /// </summary>
        public int CurrentFrame => currentFrameCount > 0 
            ? Mathf.FloorToInt((timer / currentAnimDuration) * currentFrameCount) % currentFrameCount 
            : 0;
        
        /// <summary>
        /// Total number of frames in the current animation.
        /// </summary>
        public int MaxFrame => currentFrameCount;
        
        /// <summary>
        /// Currently playing animation index.
        /// </summary>
        public int CurrentAnimationIndex => currentPlayIndex;
        
        /// <summary>
        /// Current animation duration in seconds.
        /// </summary>
        public float CurrentAnimationDuration => currentAnimDuration;
        
        /// <summary>
        /// Normalized playback position (0-1).
        /// </summary>
        public float NormalizedTime => currentAnimDuration > 0 ? timer / currentAnimDuration : 0f;
        
        /// <summary>
        /// When true, stops playback at the current frame. Use for death animations etc.
        /// </summary>
        public bool isFinish { get; set; }
        
        /// <summary>
        /// Whether the current animation is set to loop.
        /// </summary>
        public bool IsLooping => isLooping;
        
        /// <summary>
        /// The MeshFilter component (for LOD system compatibility).
        /// </summary>
        public MeshFilter meshFilter { get; private set; }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (playOnDemand && isInitialized && material != null && currentPlayIndex >= 0)
            {
                // If finished, don't update timer (freeze on last frame)
                if (isFinish)
                {
                    ScheduleBoneUpdateJob();
                    return;
                }
                
                float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
                timer += dt;

                // Check for animation completion
                if (timer >= currentAnimDuration)
                {
                    if (isLooping)
                    {
                        // Loop timer based on duration
                        timer %= currentAnimDuration;
                    }
                    else
                    {
                        // Non-looping: clamp to end and fire event once
                        timer = currentAnimDuration - 0.001f; // Clamp just before end to stay on last frame
                        
                        if (!hasReportedFinish)
                        {
                            hasReportedFinish = true;
                            OnAnimationFinished?.Invoke(currentPlayIndex);
                        }
                    }
                }
                
                material.SetFloat("_Timer", timer);
                
                // Handle blending
                if (isBlending)
                {
                    blendFromTimer += dt;
                    blendElapsed += dt;
                    
                    float blendWeight = Mathf.Clamp01(blendElapsed / blendDuration);
                    material.SetFloat("_VAT_BlendWeight", blendWeight);
                    
                    if (blendElapsed >= blendDuration)
                    {
                        isBlending = false;
                        blendFromIndex = -1;
                        material.SetFloat("_VAT_BlendWeight", 1f);
                        material.DisableKeyword("_VAT_BLEND_ON");
                    }
                }
            }
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                if (isInitialized && frameData != null && frameData.data.Length > 0)
                {
                    Play(defaultAnimationIndex);
                }
            }
        }

        private void Initialize()
        {
            if (isInitialized && material != null) return;
            
            meshFilter = GetComponent<MeshFilter>();
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
                    // Use unique material instance for SRP Batcher per-object values
                    if (material == null || material == meshRenderer.sharedMaterial)
                        material = meshRenderer.material;
                }
#else
                // Use unique material instance for SRP Batcher per-object values
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
                
                // Editor preview: set values directly on sharedMaterial (won't affect build)
                if (!Application.isPlaying && material != null && frameData != null && frameData.data.Length > 0)
                {
                    int idx = Mathf.Clamp(defaultAnimationIndex, 0, frameData.data.Length - 1);
                    var item = frameData.data[idx];
                    material.SetFloat("_Timer", 0);
                    material.SetFloat("_Frames", item.frames);
                    material.SetFloat("_Offset", item.offset);
                    material.SetFloat("_Duration", item.duration);
                }
            }
        }


        /// <summary>
        /// Play an animation by name with explicit loop control and optional randomization override.
        /// </summary>
        public void Play(string name, bool loop, bool? useRandomOffset = null)
        {
            if (frameData == null) return;
            int index = frameData.GetIndexByName(name);
            if (index >= 0)
            {
                Play(index, loop, useRandomOffset);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[AnimationFramePlayer] Animation '{name}' not found on {gameObject.name}");
            }
        }

        /// <summary>
        /// Transition to a new animation by name with blending.
        /// </summary>
        public void PlayTransition(string name, float duration)
        {
            if (frameData == null) return;
            int index = frameData.GetIndexByName(name);
            if (index >= 0)
            {
                PlayTransition(index, duration);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[AnimationFramePlayer] Animation '{name}' not found on {gameObject.name} for transition");
            }
        }

        /// <summary>
        /// Play an animation by index with explicit loop control and optional randomization override.
        /// </summary>
        /// <param name="index">Animation index from frameData.</param>
        /// <param name="loop">If false, animation plays once then fires OnAnimationFinished.</param>
        /// <param name="useRandomOffset">Override for random start offset. If null, uses component default.</param>
        public void Play(int index, bool loop = true, bool? useRandomOffset = null)
        {
            Initialize(); 
            if (material == null) return;
            if (currentPlayIndex == index && !playOnDemand) return;

            currentPlayIndex = index;
            timer = 0f;

            bool shouldRandomize = useRandomOffset ?? randomStartOffset;
            
            if (shouldRandomize && frameData != null && index >= 0 && index < frameData.data.Length)
            {
                timer = UnityEngine.Random.Range(0f, frameData.data[index].duration);
            }

            isBlending = false;
            isLooping = loop;
            isFinish = false;
            hasReportedFinish = false;

            if (currentPlayIndex < 0 || frameData == null || currentPlayIndex >= frameData.data.Length)
            {
                UpdateMaterial();
            }
            else
            {
                var item = frameData.data[currentPlayIndex];
                currentAnimDuration = item.duration;
                currentFrameCount = item.frames;
                UpdateMaterial(item.frames, item.offset, item.duration);
            }
        }
        
        /// <summary>
        /// Transition to a new animation with blending.
        /// </summary>
        public void PlayTransition(int targetIndex, float duration)
        {
            if (targetIndex == currentPlayIndex || frameData == null || targetIndex >= frameData.data.Length) return;
            
            Initialize();
            if (material == null) return;
            
            // Setup blend from current state
            blendFromIndex = currentPlayIndex;
            blendFromTimer = timer;
            blendDuration = Mathf.Max(0.001f, duration);
            blendElapsed = 0f;
            isBlending = true;
            material.EnableKeyword("_VAT_BLEND_ON");
            
            // Set blend source parameters
            if (blendFromIndex >= 0 && blendFromIndex < frameData.data.Length)
            {
                var fromItem = frameData.data[blendFromIndex];
                material.SetFloat("_VAT_BlendDuration", fromItem.duration);
                material.SetFloat("_VAT_BlendFrames", fromItem.frames);
                material.SetFloat("_VAT_BlendOffset", fromItem.offset);
            }
            material.SetFloat("_VAT_BlendWeight", 0f);
            
            // Start playing target
            currentPlayIndex = targetIndex;
            timer = 0f;
            
            var item = frameData.data[currentPlayIndex];
            currentAnimDuration = item.duration;
            currentFrameCount = item.frames;
            UpdateMaterial(item.frames, item.offset, item.duration);
        }

        private void UpdateMaterial(int frames = 0, int offset = 0, float duration = 0)
        {
            if (material == null) return;

            material.EnableKeyword("_VAT_ON");

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
            material.SetVector("_AnimDurationAndOffset", new Vector4(duration, offset, 0, 0));
            material.SetFloat("_Frames", frames);
            material.SetFloat("_Offset", offset);
            material.SetFloat("_Duration", duration);
        }
        
        private void OnEnable()
        {
            InitializeBoneJobs();
            if (BoneUpdateManager.Instance != null)
            {
                BoneUpdateManager.Instance.Register(this);
            }
        }

        private void OnDisable()
        {
            if (BoneUpdateManager.Instance != null)
            {
                BoneUpdateManager.Instance.Unregister(this);
            }
            DisposeBoneJobs();
        }

        private void OnDestroy()
        {
            DisposeBoneJobs();
        }

        private void InitializeBoneJobs()
        {
            if (m_BoneJobsInitialized) return;
            if (animatedBones == null || animatedBones.Length == 0) return;
            if (frameData == null || frameData.boneFrames == null || frameData.boneCount == 0) return;

            // Filter out null transforms and build TransformAccessArray
            var validTransforms = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < animatedBones.Length && i < frameData.boneCount; i++)
            {
                if (animatedBones[i] != null)
                    validTransforms.Add(animatedBones[i]);
            }

            if (validTransforms.Count == 0) return;

            m_BoneTransformAccess = new TransformAccessArray(validTransforms.ToArray());

            // Acquire shared native data from cache
            NativeBoneDataCache.Acquire(frameData, out m_NativeBoneFrames, out m_NativeAnimTimings);

            m_BoneJobsInitialized = m_NativeBoneFrames.IsCreated && m_NativeAnimTimings.IsCreated;
        }

        private void DisposeBoneJobs()
        {
            if (!m_BoneJobsInitialized) return;

            if (m_BoneTransformAccess.isCreated)
                m_BoneTransformAccess.Dispose();

            NativeBoneDataCache.Release(frameData);

            m_NativeBoneFrames = default;
            m_NativeAnimTimings = default;
            m_BoneJobsInitialized = false;
        }

        /// <summary>
        /// Schedules the bone update job and returns the handle.
        /// Does NOT call Complete() - completion is managed by BoneUpdateManager.
        /// </summary>
        public Unity.Jobs.JobHandle? GetBoneUpdateHandle()
        {
            if (!playOnDemand || !isInitialized || material == null || currentPlayIndex < 0) return null;

            if (!m_BoneJobsInitialized)
            {
                InitializeBoneJobs();
                if (!m_BoneJobsInitialized) return null;
            }

            if (currentPlayIndex < 0 || currentPlayIndex >= frameData.data.Length) return null;

            // Calculate current frame
            float normalizedTime = timer / currentAnimDuration;
            int frame = Mathf.FloorToInt(normalizedTime * currentFrameCount) % currentFrameCount;

            // Calculate blend-from frame if blending
            int blendFromFrame = 0;
            if (isBlending && blendFromIndex >= 0 && blendFromIndex < frameData.data.Length)
            {
                var fromItem = frameData.data[blendFromIndex];
                float fromNormalizedTime = blendFromTimer / fromItem.duration;
                blendFromFrame = Mathf.FloorToInt(fromNormalizedTime * fromItem.frames) % fromItem.frames;
            }

            var jobParams = new BoneJobParams
            {
                CurrentAnimIndex = currentPlayIndex,
                CurrentFrame = frame,
                BlendFromAnimIndex = blendFromIndex,
                BlendFromFrame = blendFromFrame,
                BlendWeight = Mathf.Clamp01(blendElapsed / blendDuration),
                IsBlending = isBlending
            };

            var job = new UpdateBoneTransformsJob
            {
                BoneFrames = m_NativeBoneFrames,
                AnimTimings = m_NativeAnimTimings,
                BoneCount = frameData.boneCount,
                Params = jobParams
            };

            return job.Schedule(m_BoneTransformAccess);
        }

        private void ScheduleBoneUpdateJob()
        {
            // Legacy/Self-update fallback: if manager is missing, we must complete our own job
            var handle = GetBoneUpdateHandle();
            if (handle.HasValue)
            {
                handle.Value.Complete();
            }
        }

        /// <summary>
        /// Automatically finds all immediate child transforms and assigns them to the animatedBones array.
        /// </summary>
        public void ScanBones()
        {
            int childCount = transform.childCount;
            animatedBones = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                animatedBones[i] = transform.GetChild(i);
            }
        }
    }
}
