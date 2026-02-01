using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    /// <summary>
    /// Burst-compiled baking engine for high-performance vertex data processing.
    /// Uses Jobs and NativeArrays to avoid GC allocations and leverage SIMD.
    /// </summary>
    public static class BurstBakerEngine
    {
        /// <summary>
        /// Packed vertex info for Burst processing (matches compute shader layout).
        /// </summary>
        public struct VertInfoNative
        {
            public float3 position;
            public float3 normal;
            public float3 tangent;
            public float3 velocity;
        }

        /// <summary>
        /// Burst-compiled job for rotating and packing vertex data.
        /// </summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        public struct ProcessVertexDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<float3> Normals;
            [ReadOnly] public NativeArray<float4> Tangents;
            [ReadOnly] public NativeArray<float3> Velocities;
            [ReadOnly] public float3 RotateEuler;

            [WriteOnly] public NativeArray<VertInfoNative> Output;

            public void Execute(int index)
            {
                float3 pos = Positions[index];
                float3 nrm = Normals[index];
                float4 tan = Tangents[index];
                float3 vel = Velocities[index];

                // Apply rotation
                quaternion rotX = quaternion.RotateX(math.radians(RotateEuler.x));
                quaternion rotY = quaternion.RotateY(math.radians(RotateEuler.y));
                quaternion rotZ = quaternion.RotateZ(math.radians(RotateEuler.z));
                quaternion rot = math.mul(math.mul(rotZ, rotY), rotX);

                Output[index] = new VertInfoNative
                {
                    position = math.rotate(rot, pos),
                    normal = math.rotate(rot, nrm),
                    tangent = math.rotate(rot, tan.xyz),
                    velocity = math.rotate(rot, vel)
                };
            }
        }

        /// <summary>
        /// Burst-compiled job for averaging multiple samples (temporal blur).
        /// </summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        public struct AverageVertexSamplesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> AccumulatedPositions;
            [ReadOnly] public NativeArray<float3> AccumulatedNormals;
            [ReadOnly] public NativeArray<float4> AccumulatedTangents;
            [ReadOnly] public NativeArray<float3> Velocities;
            [ReadOnly] public int SampleCount;
            [ReadOnly] public float3 RotateEuler;

            [WriteOnly] public NativeArray<VertInfoNative> Output;

            public void Execute(int index)
            {
                float invSamples = 1f / SampleCount;
                float3 pos = AccumulatedPositions[index] * invSamples;
                float3 nrm = math.normalize(AccumulatedNormals[index] * invSamples);
                float3 tan = math.normalize(AccumulatedTangents[index].xyz * invSamples);
                float3 vel = Velocities[index];

                // Apply rotation
                quaternion rotX = quaternion.RotateX(math.radians(RotateEuler.x));
                quaternion rotY = quaternion.RotateY(math.radians(RotateEuler.y));
                quaternion rotZ = quaternion.RotateZ(math.radians(RotateEuler.z));
                quaternion rot = math.mul(math.mul(rotZ, rotY), rotX);

                Output[index] = new VertInfoNative
                {
                    position = math.rotate(rot, pos),
                    normal = math.rotate(rot, nrm),
                    tangent = math.rotate(rot, tan),
                    velocity = math.rotate(rot, vel)
                };
            }
        }

        /// <summary>
        /// Burst-compiled job to accumulate vertex samples.
        /// </summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        public struct AccumulateVertexSamplesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> SourcePositions;
            [ReadOnly] public NativeArray<float3> SourceNormals;
            [ReadOnly] public NativeArray<float4> SourceTangents;

            public NativeArray<float3> AccumulatedPositions;
            public NativeArray<float3> AccumulatedNormals;
            public NativeArray<float4> AccumulatedTangents;

            public void Execute(int index)
            {
                AccumulatedPositions[index] += SourcePositions[index];
                AccumulatedNormals[index] += SourceNormals[index];
                AccumulatedTangents[index] += SourceTangents[index];
            }
        }

        /// <summary>
        /// Helper to convert NativeArray of VertInfoNative to the legacy VertInfo array.
        /// </summary>
        public static BakerEngine.VertInfo[] ToLegacyArray(NativeArray<VertInfoNative> native)
        {
            var result = new BakerEngine.VertInfo[native.Length];
            for (int i = 0; i < native.Length; i++)
            {
                result[i] = new BakerEngine.VertInfo
                {
                    position = native[i].position,
                    normal = native[i].normal,
                    tangent = native[i].tangent,
                    velocity = native[i].velocity
                };
            }
            return result;
        }
    }
}
