using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    /// <summary>
    /// Static cache that provides shared NativeArrays for AnimationCombinedFrames assets.
    /// Prevents redundant allocations when multiple characters share the same animation set.
    /// </summary>
    public static class NativeBoneDataCache
    {
        private struct CachedData
        {
            public NativeArray<NativeBoneFrameData> BoneFrames;
            public NativeArray<NativeAnimTiming> AnimTimings;
            public int RefCount;
        }

        private static readonly Dictionary<int, CachedData> s_Cache = new Dictionary<int, CachedData>();

        /// <summary>
        /// Acquire native arrays for the given frame data asset. Increments reference count.
        /// </summary>
        public static void Acquire(
            AnimationCombinedFrames frameData,
            out NativeArray<NativeBoneFrameData> boneFrames,
            out NativeArray<NativeAnimTiming> animTimings)
        {
            if (frameData == null || frameData.boneFrames == null || frameData.boneCount == 0)
            {
                boneFrames = default;
                animTimings = default;
                return;
            }

            int key = frameData.GetInstanceID();

            if (s_Cache.TryGetValue(key, out var cached))
            {
                cached.RefCount++;
                s_Cache[key] = cached;
                boneFrames = cached.BoneFrames;
                animTimings = cached.AnimTimings;
                return;
            }

            // Create new native arrays from managed data
            int boneFrameCount = frameData.boneFrames.Length;
            var nativeBoneFrames = new NativeArray<NativeBoneFrameData>(boneFrameCount, Allocator.Persistent);

            for (int i = 0; i < boneFrameCount; i++)
            {
                var src = frameData.boneFrames[i];
                nativeBoneFrames[i] = new NativeBoneFrameData
                {
                    Position = src.position,
                    Rotation = src.rotation
                };
            }

            // Build animation timing array
            int animCount = frameData.data.Length;
            var nativeAnimTimings = new NativeArray<NativeAnimTiming>(animCount, Allocator.Persistent);
            int frameOffset = 0;

            for (int i = 0; i < animCount; i++)
            {
                nativeAnimTimings[i] = new NativeAnimTiming
                {
                    FrameOffset = frameOffset,
                    FrameCount = frameData.data[i].frames
                };
                frameOffset += frameData.data[i].frames;
            }

            var newCached = new CachedData
            {
                BoneFrames = nativeBoneFrames,
                AnimTimings = nativeAnimTimings,
                RefCount = 1
            };
            s_Cache[key] = newCached;

            boneFrames = nativeBoneFrames;
            animTimings = nativeAnimTimings;
        }

        /// <summary>
        /// Release reference to native arrays. Disposes when reference count reaches zero.
        /// </summary>
        public static void Release(AnimationCombinedFrames frameData)
        {
            if (frameData == null) return;

            int key = frameData.GetInstanceID();

            if (!s_Cache.TryGetValue(key, out var cached)) return;

            cached.RefCount--;

            if (cached.RefCount <= 0)
            {
                if (cached.BoneFrames.IsCreated) cached.BoneFrames.Dispose();
                if (cached.AnimTimings.IsCreated) cached.AnimTimings.Dispose();
                s_Cache.Remove(key);
            }
            else
            {
                s_Cache[key] = cached;
            }
        }

        /// <summary>
        /// Dispose all cached data. Call on application quit or domain reload.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DisposeAll()
        {
            foreach (var kvp in s_Cache)
            {
                if (kvp.Value.BoneFrames.IsCreated) kvp.Value.BoneFrames.Dispose();
                if (kvp.Value.AnimTimings.IsCreated) kvp.Value.AnimTimings.Dispose();
            }
            s_Cache.Clear();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor: Force refresh cache for a specific asset (after re-baking).
        /// </summary>
        public static void InvalidateCache(AnimationCombinedFrames frameData)
        {
            if (frameData == null) return;

            int key = frameData.GetInstanceID();
            if (s_Cache.TryGetValue(key, out var cached))
            {
                if (cached.BoneFrames.IsCreated) cached.BoneFrames.Dispose();
                if (cached.AnimTimings.IsCreated) cached.AnimTimings.Dispose();
                s_Cache.Remove(key);
            }
        }
#endif
    }
}
