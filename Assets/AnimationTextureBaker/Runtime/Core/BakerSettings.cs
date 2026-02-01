using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    [System.Serializable]
    public class BakerSettings
    {
        [Tooltip("Compute shader used for generating vertex info textures.")]
        public ComputeShader infoTexGen;
        [Tooltip("Shader used for playing animations (Vertex Texture Fetch).")]
        public Shader playShader;
        [Tooltip("List of animation clips to bake.")]
        public AnimationClip[] clips;

        
        [Tooltip("The frequency of animation sampling (frames per second). The total frame count will be rounded up to the nearest power of two.")]
        [Range(2, 60)]
        public int frameRate = 30;

        [Header("Performance")]
        [Tooltip("Use Burst-compiled jobs for faster baking (requires Burst package).")]
        public bool useBurstBaking = true;

        public enum TexturePrecision { Half16, Float32 }
        [Tooltip("Texture precision. Half16 uses 50% less VRAM with minimal quality loss.")]
        public TexturePrecision texturePrecision = TexturePrecision.Half16;



        
        [Tooltip("The folder in Assets where baked textures and materials will be saved.")]
        public string saveToFolder = "AnimationBakerOutput";
        [Tooltip("Generate ready-to-use prefabs for each clip.")]
        public bool createPrefabs = false;
        [Tooltip("Save a static copy of the mesh with custom bounds.")]
        public bool createMeshAsset = false;
        [Tooltip("Run Unity Mesh Optimizer on the saved mesh.")]
        public bool optimizeMeshOnSave = false;
        [Tooltip("Collapses the entire mesh into a single bounding box. This makes the model harder to reverse-engineer while keeping the vertex animation data intact.")]
        public bool collapseMesh = false;

        [Tooltip("Combine multiple clips into a single texture atlas.")]
        public bool combineTextures = false;

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
