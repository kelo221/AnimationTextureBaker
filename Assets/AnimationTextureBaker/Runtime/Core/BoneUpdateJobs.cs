using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Kelo.AnimationTextureBaker
{
    /// <summary>
    /// Per-instance parameters passed to the bone update job each frame.
    /// </summary>
    public struct BoneJobParams
    {
        public int CurrentAnimIndex;
        public int CurrentFrame;
        public int BlendFromAnimIndex;
        public int BlendFromFrame;
        public float BlendWeight;
        public bool IsBlending;
    }

    /// <summary>
    /// Native-compatible bone frame data for Burst jobs.
    /// </summary>
    public struct NativeBoneFrameData
    {
        public float3 Position;
        public quaternion Rotation;
    }

    /// <summary>
    /// Animation timing metadata for frame offset calculations.
    /// </summary>
    public struct NativeAnimTiming
    {
        public int FrameOffset;
        public int FrameCount;
    }

    /// <summary>
    /// Burst-compiled job for updating bone transforms in parallel.
    /// Each index corresponds to one bone transform.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct UpdateBoneTransformsJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<NativeBoneFrameData> BoneFrames;
        [ReadOnly] public NativeArray<NativeAnimTiming> AnimTimings;
        [ReadOnly] public int BoneCount;
        [ReadOnly] public BoneJobParams Params;

        public void Execute(int boneIndex, TransformAccess transform)
        {
            if (boneIndex >= BoneCount) return;

            // Get current animation bone data
            int currentGlobalFrame = AnimTimings[Params.CurrentAnimIndex].FrameOffset + Params.CurrentFrame;
            int currentDataIndex = currentGlobalFrame * BoneCount + boneIndex;

            NativeBoneFrameData boneData;
            if (currentDataIndex >= 0 && currentDataIndex < BoneFrames.Length)
            {
                boneData = BoneFrames[currentDataIndex];
            }
            else
            {
                boneData = new NativeBoneFrameData
                {
                    Position = float3.zero,
                    Rotation = quaternion.identity
                };
            }

            // Blend with previous animation if active
            if (Params.IsBlending && Params.BlendFromAnimIndex >= 0 && Params.BlendFromAnimIndex < AnimTimings.Length)
            {
                int fromGlobalFrame = AnimTimings[Params.BlendFromAnimIndex].FrameOffset + Params.BlendFromFrame;
                int fromDataIndex = fromGlobalFrame * BoneCount + boneIndex;

                if (fromDataIndex >= 0 && fromDataIndex < BoneFrames.Length)
                {
                    var fromBoneData = BoneFrames[fromDataIndex];
                    float t = Params.BlendWeight;

                    boneData.Position = math.lerp(fromBoneData.Position, boneData.Position, t);
                    boneData.Rotation = math.slerp(fromBoneData.Rotation, boneData.Rotation, t);
                }
            }

            transform.localPosition = boneData.Position;
            transform.localRotation = boneData.Rotation;
        }
    }
}
