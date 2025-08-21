using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// Efficient pooling system for AnimationClipPlayables
    /// Reduces allocation overhead by reusing playables
    /// </summary>
    internal class PlayablePool
    {
        private readonly PlayableGraph graph;
        private readonly Dictionary<int, Stack<AnimationClipPlayable>> pools;
        private readonly Dictionary<int, int> poolSizes;
        private readonly int maxPoolSizePerClip;
        
        private int totalPooledCount;
        private int totalRentedCount;
        
        #region Initialization
        
        public PlayablePool(PlayableGraph graph, int maxPoolSizePerClip = 4)
        {
            this.graph = graph;
            this.maxPoolSizePerClip = maxPoolSizePerClip;
            
            pools = new Dictionary<int, Stack<AnimationClipPlayable>>(AnimationConstants.MAX_UNIQUE_CLIPS);
            poolSizes = new Dictionary<int, int>(AnimationConstants.MAX_UNIQUE_CLIPS);
        }
        
        #endregion
        
        #region Pool Operations
        
        /// <summary>
        /// Rent a playable from the pool
        /// </summary>
        public AnimationClipPlayable Rent(AnimationClip clip)
        {
            if (clip == null || !graph.IsValid())
                return default(AnimationClipPlayable);
            
            int clipID = clip.GetInstanceID();
            
            // Get or create pool for this clip
            if (!pools.TryGetValue(clipID, out var pool))
            {
                pool = new Stack<AnimationClipPlayable>(maxPoolSizePerClip);
                pools[clipID] = pool;
                poolSizes[clipID] = 0;
            }
            
            AnimationClipPlayable playable;
            
            // Try to get from pool
            if (pool.Count > 0)
            {
                playable = pool.Pop();
                totalPooledCount--;
                
                // Reset playable state
                if (playable.IsValid())
                {
                    playable.SetTime(0);
                    playable.SetDone(false);
                    playable.SetSpeed(1f);
                    totalRentedCount++;
                    return playable;
                }
            }
            
            // Create new if pool is empty
            playable = AnimationClipPlayable.Create(graph, clip);
            totalRentedCount++;
            
            return playable;
        }
        
        /// <summary>
        /// Return a playable to the pool
        /// </summary>
        /*public void Return(AnimationClipPlayable playable, int clipID)
        {
            if (!playable.IsValid())
                return;
            
            // Reset state
            playable.SetTime(0);
            playable.SetDone(false);
            playable.SetSpeed(1f);
            
            // Disconnect from graph
            /*if (graph.IsValid())
            {
                for (int i = 0; i < playable.GetOutputCount(); i++)
                {
                    if (playable.GetOutput(i).IsOutputValid())
                        graph.Disconnect(playable, i);
                }
            } #1#// proposed by claude with errors
            
            if (graph.IsValid())
            {
                // Proper way to disconnect playable outputs
                var playableOutput = playable.GetOutput(0);
                if (playableOutput.IsOutputValid())
                {
                    playableOutput.SetSourcePlayable(Playable.Null);
                }
            }
            
            /*if (graph.IsValid())
            {
                for (int i = playable.GetOutputCount() - 1; i >= 0; i--)
                    graph.Disconnect(playable, i);
            } //proposed by ChatGPT#1#
            
            // Return to pool if not at capacity
            if (pools.TryGetValue(clipID, out var pool))
            {
                if (pool.Count < maxPoolSizePerClip)
                {
                    pool.Push(playable);
                    totalPooledCount++;
                    totalRentedCount--;
                    return;
                }
            }
            
            // Destroy if pool is full
            playable.Destroy();
            totalRentedCount--;
        }*/
        
        public void Return(AnimationClipPlayable playable, int clipID)
        {
            if (!playable.IsValid())
                return;

            // Reset state
            playable.SetTime(0);
            playable.SetDone(false);
            playable.SetSpeed(1f);

            // No need for manual disconnection - Unity handles this
            // Just return to pool if not at capacity
            if (pools.TryGetValue(clipID, out var pool))
            {
                if (pool.Count < maxPoolSizePerClip)
                {
                    pool.Push(playable);
                    totalPooledCount++;
                    totalRentedCount--;
                    return;
                }
            }

            // Destroy if pool is full
            playable.Destroy();
            totalRentedCount--;
        }

        
        /// <summary>
        /// Prewarm pool with instances for a clip
        /// </summary>
        public void Prewarm(AnimationClip clip, int count)
        {
            if (clip == null || !graph.IsValid())
                return;
            
            int clipID = clip.GetInstanceID();
            
            // Get or create pool
            if (!pools.TryGetValue(clipID, out var pool))
            {
                pool = new Stack<AnimationClipPlayable>(maxPoolSizePerClip);
                pools[clipID] = pool;
                poolSizes[clipID] = 0;
            }
            
            // Create instances up to count or max size
            int toCreate = UnityEngine.Mathf.Min(count, maxPoolSizePerClip - pool.Count);
            
            for (int i = 0; i < toCreate; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                if (playable.IsValid())
                {
                    pool.Push(playable);
                    totalPooledCount++;
                }
            }
            
            poolSizes[clipID] = pool.Count;
        }
        
        /// <summary>
        /// Clear pool for specific clip
        /// </summary>
        public void ClearPool(int clipID)
        {
            if (pools.TryGetValue(clipID, out var pool))
            {
                while (pool.Count > 0)
                {
                    var playable = pool.Pop();
                    if (playable.IsValid())
                    {
                        playable.Destroy();
                    }
                    totalPooledCount--;
                }
                
                pools.Remove(clipID);
                poolSizes.Remove(clipID);
            }
        }
        
        /// <summary>
        /// Clear all pools
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in pools)
            {
                var pool = kvp.Value;
                while (pool.Count > 0)
                {
                    var playable = pool.Pop();
                    if (playable.IsValid())
                    {
                        playable.Destroy();
                    }
                }
            }
            
            pools.Clear();
            poolSizes.Clear();
            totalPooledCount = 0;
            totalRentedCount = 0;
        }
        
        /// <summary>
        /// Trim excess pooled instances
        /// </summary>
        public void TrimExcess()
        {
            foreach (var kvp in pools)
            {
                var pool = kvp.Value;
                int clipID = kvp.Key;
                
                // Keep only half of pooled instances
                int targetSize = pool.Count / 2;
                
                while (pool.Count > targetSize)
                {
                    var playable = pool.Pop();
                    if (playable.IsValid())
                    {
                        playable.Destroy();
                    }
                    totalPooledCount--;
                }
                
                poolSizes[clipID] = pool.Count;
            }
        }
        
        #endregion
        
        #region Statistics
        
        /// <summary>
        /// Get pool statistics
        /// </summary>
        public PoolStatistics GetStatistics()
        {
            return new PoolStatistics
            {
                TotalPooled = totalPooledCount,
                TotalRented = totalRentedCount,
                PoolCount = pools.Count,
                TotalCapacity = pools.Count * maxPoolSizePerClip
            };
        }
        
        public struct PoolStatistics
        {
            public int TotalPooled;
            public int TotalRented;
            public int PoolCount;
            public int TotalCapacity;
            
            public override string ToString()
            {
                return $"Pooled:{TotalPooled} Rented:{TotalRented} Pools:{PoolCount} Capacity:{TotalCapacity}";
            }
        }
        
        #endregion
        
        #region Memory Management
        
        /// <summary>
        /// Estimate memory usage in bytes
        /// </summary>
        public int EstimateMemoryUsage()
        {
            // Rough estimate: each playable ~1KB + dictionary overhead
            int playableMemory = (totalPooledCount + totalRentedCount) * 1024;
            int dictionaryOverhead = pools.Count * 64; // Estimate for dictionary entries
            
            return playableMemory + dictionaryOverhead;
        }
        
        /// <summary>
        /// Check if should trim based on memory pressure
        /// </summary>
        public bool ShouldTrim()
        {
            // Trim if we have more than 50% capacity unused
            float utilization = (float)totalRentedCount / (totalPooledCount + totalRentedCount + 1);
            return utilization < 0.5f && totalPooledCount > 4;
        }
        
        #endregion
    }
}