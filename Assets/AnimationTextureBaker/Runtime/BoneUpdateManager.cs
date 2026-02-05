using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace Kelo.AnimationTextureBaker
{
    /// <summary>
    /// Batches bone update jobs for all active AnimationFramePlayer instances.
    /// This prevents main-thread stalls caused by per-instance JobHandle.Complete() calls.
    /// </summary>
    public class BoneUpdateManager : MonoBehaviour
    {
        private static BoneUpdateManager _instance;
        public static BoneUpdateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<BoneUpdateManager>();
                }
                return _instance;
            }
        }

        private readonly List<AnimationFramePlayer> m_Players = new List<AnimationFramePlayer>();
        private JobHandle m_CombinedJobHandle;
        private bool m_HasPendingJobs;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        public void Register(AnimationFramePlayer player)
        {
            if (!m_Players.Contains(player))
            {
                m_Players.Add(player);
            }
        }

        public void Unregister(AnimationFramePlayer player)
        {
            m_Players.Remove(player);
        }

        private void LateUpdate()
        {
            ScheduleBatch();
            CompleteBatch();
        }

        private void ScheduleBatch()
        {
            if (m_Players.Count == 0) return;

            for (int i = 0; i < m_Players.Count; i++)
            {
                var player = m_Players[i];
                if (player == null || !player.isActiveAndEnabled) continue;

                var handle = player.GetBoneUpdateHandle();
                if (handle.HasValue)
                {
                    m_CombinedJobHandle = JobHandle.CombineDependencies(m_CombinedJobHandle, handle.Value);
                    m_HasPendingJobs = true;
                }
            }
        }

        private void CompleteBatch()
        {
            if (m_HasPendingJobs)
            {
                m_CombinedJobHandle.Complete();
                m_CombinedJobHandle = default;
                m_HasPendingJobs = false;
            }
        }
        
        private void OnDestroy()
        {
            CompleteBatch();
            if (_instance == this) _instance = null;
        }
    }
}
