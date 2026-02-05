using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    /// <summary>
    /// Animated bone entry supporting single bone or limb pair mode.
    /// For limb pairs, computes midpoint position and axis-aligned rotation.
    /// </summary>
    [System.Serializable]
    public class AnimatedBoneEntry
    {
        [Tooltip("Primary bone (required)")]
        public Transform boneA;
        [Tooltip("Secondary bone (optional). If set, computes midpoint position and axis-aligned rotation between both bones.")]
        public Transform boneB;
        
        public bool IsLimbPair => boneA != null && boneB != null;
        public bool IsValid => boneA != null;
    }

    /// <summary>
    /// Clip entry supporting primary animation with optional secondary layer + mask.
    /// </summary>
    [System.Serializable]
    public class ClipEntry
    {
        [Tooltip("Primary animation clip (required)")]
        public AnimationClip primary;
        [Tooltip("Secondary animation clip (optional). If set with mask, blends using avatar mask.")]
        public AnimationClip secondary;
        [Tooltip("Avatar mask for blending. Masked bones use secondary animation, unmasked use primary.")]
        public AvatarMask mask;
        
        public bool HasMaskedLayer => secondary != null && mask != null;
        public bool IsValid => primary != null;
        
        /// <summary>
        /// Returns the primary clip duration.
        /// </summary>
        public float Duration => primary != null ? primary.length : 0f;
        
        /// <summary>
        /// Returns the primary clip name.
        /// </summary>
        public string Name => primary != null ? primary.name : "";
    }

    [System.Serializable]
    public class BakerSettings
    {
        [Tooltip("Compute shader used for generating vertex info textures.")]
        public ComputeShader infoTexGen;
        [Tooltip("Shader used for playing animations (Vertex Texture Fetch).")]
        public Shader playShader;
        
        [Header("Animation Clips")]
        [Tooltip("List of animation clips to bake. Use Primary + Secondary + Mask for layered blending.")]
        public ClipEntry[] clips = new ClipEntry[0];

        [Tooltip("The frequency of animation sampling (frames per second). The total frame count will be rounded up to the nearest power of two.")]
        [Range(2, 60)]
        public int frameRate = 60;

        [Header("Performance")]
        [Tooltip("Use Burst-compiled jobs for faster baking (requires Burst package).")]
        public bool useBurstBaking = true;

        public enum TexturePrecision { Half16, Float32 }
        [Tooltip("Texture precision. Half16 uses 50% less VRAM with minimal quality loss.")]
        public TexturePrecision texturePrecision = TexturePrecision.Half16;

        [Tooltip("The folder in Assets where baked textures and materials will be saved.")]
        public string saveToFolder = "AnimationBakerOutput";
        [Tooltip("Generate ready-to-use prefabs for each clip.")]
        public bool createPrefabs = true;
        [Tooltip("Save a static copy of the mesh with custom bounds.")]
        public bool createMeshAsset = false;
        [Tooltip("Run Unity Mesh Optimizer on the saved mesh.")]
        public bool optimizeMeshOnSave = true;
        [Tooltip("Collapses the entire mesh into a single bounding box. This makes the model harder to reverse-engineer while keeping the vertex animation data intact.")]
        public bool collapseMesh = false;

        [Tooltip("Combine multiple clips into a single texture atlas.")]
        public bool combineTextures = true;

        [Header("Motion Blur")]
        [Tooltip("Bakes velocity vectors into a separate texture. Required for Unity's Motion Blur post-processing to work with custom shaders.")]
        public bool bakeVelocity = false;
        [Tooltip("Artificial exposure time for velocity calculation and averaging (e.g., 1/48 = 0.0208).")]
        public float exposure = 1f / 48f;
        [Tooltip("Number of sub-frame samples to average for 'baked' motion blur. Higher values create a smoother 'mean' position but slow down baking.")]
        [Range(1, 16)]
        public int blurSamples = 1;

        [Tooltip("Additional rotation applied to vertices during bake.")]
        public Vector3 rotate = Vector3.zero;
        [Tooltip("Multiplier for the mesh bounds size.")]
        public float boundsScale = 1;

        [Header("Bone Attachments")]
        [Tooltip("Animated bone entries. Set only Bone A for single-bone tracking. Set both Bone A and Bone B for limb pairs (auto-computes midpoint and rotation).")]
        public AnimatedBoneEntry[] animatedBones = new AnimatedBoneEntry[0];

        [System.Serializable]
        public class ShaderKeywords
        {
            public string mainTexName = "_MainTex";
            public string posTexName = "_PosTex";
            public string normTexName = "_NmlTex";
            public string tanTexName = "_TanTex";
            public string velTexName = "_VelTex";
        }

        public ShaderKeywords keywords = new ShaderKeywords();
    }
}

