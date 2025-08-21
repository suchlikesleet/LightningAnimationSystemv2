using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// SIMD-friendly animation state structure
    /// Uses float4/int4 for efficient batch processing
    /// </summary>
    [Serializable]
    internal struct AnimationState
    {
        // SIMD-friendly data layout using Unity.Mathematics
        public float4 TimeData;      // x: currentTime, y: length, z: speed, w: normalizedTime
        public float4 WeightData;    // x: current, y: target, z: blendSpeed, w: reserved
        public int4 Metadata;        // x: clipID, y: version, z: loopCount, w: flags
        public int4 References;      // x: playableIndex, y: maxLoops, z: eventIndex, w: reserved
        
        // Direct references (not SIMD processed)
        public AnimationClipPlayable Playable;
        public Action OnComplete;
        public Action OnLoop;
        
        /// <summary>
        /// Initialize state with clip data
        /// </summary>
        public void Initialize(AnimationClip clip, int clipID, int version, int playableIndex)
        {
            TimeData = new float4(0f, clip.length, 1f, 0f);
            WeightData = new float4(0f, 1f, AnimationConstants.DEFAULT_BLEND_SPEED, 0f);
            Metadata = new int4(clipID, version, 0, (int)AnimationFlags.Initialized);
            References = new int4(playableIndex, -1, -1, 0);
            
            OnComplete = null;
            OnLoop = null;
        }
        
        /// <summary>
        /// Reset state for reuse
        /// </summary>
        public void Reset()
        {
            TimeData = float4.zero;
            WeightData = new float4(0f, 1f, AnimationConstants.DEFAULT_BLEND_SPEED, 0f);
            Metadata.z = 0; // Reset loop count
            Metadata.w = (int)AnimationFlags.None; // Clear flags
            References.y = -1; // Reset max loops
            
            OnComplete = null;
            OnLoop = null;
        }
        
        /// <summary>
        /// Update time with SIMD-friendly operations
        /// </summary>
        public void UpdateTime(float deltaTime)
        {
            if (!IsPlaying || IsPaused) return;
            
            // SIMD-friendly time update
            TimeData.x = math.mad(deltaTime, TimeData.z, TimeData.x); // time += delta * speed
            
            // Update normalized time
            if (TimeData.y > 0)
            {
                TimeData.w = TimeData.x / TimeData.y;
                
                // Handle looping
                if (TimeData.w >= 1f)
                {
                    if (IsLooping)
                    {
                        Metadata.z++; // Increment loop count
                        TimeData.x = math.fmod(TimeData.x, TimeData.y);
                        TimeData.w = TimeData.x / TimeData.y;
                        OnLoop?.Invoke();
                        
                        // Check max loops
                        if (References.y > 0 && Metadata.z >= References.y)
                        {
                            SetFlag(AnimationFlags.Playing, false);
                            OnComplete?.Invoke();
                        }
                    }
                    else
                    {
                        TimeData.x = TimeData.y;
                        TimeData.w = 1f;
                        SetFlag(AnimationFlags.Playing, false);
                        OnComplete?.Invoke();
                    }
                }
            }
        }
        
        /// <summary>
        /// Update weight with blending
        /// </summary>
        public void UpdateWeight(float deltaTime)
        {
            if (IsFadingIn || IsFadingOut)
            {
                float blendDelta = WeightData.z * deltaTime;
                //WeightData.x = math.movetowards(WeightData.x, WeightData.y, blendDelta); // please check this as i was getting errors movetowards unable to resolve symbols
                WeightData.x = math.lerp(WeightData.x, WeightData.y, blendDelta);
                
                // Clear fade flags when target reached
                if (math.abs(WeightData.x - WeightData.y) < AnimationConstants.MIN_WEIGHT_THRESHOLD)
                {
                    WeightData.x = WeightData.y;
                    SetFlag(AnimationFlags.FadingIn | AnimationFlags.FadingOut, false);
                }
            }
        }
        
        #region Flag Operations (Optimized bit operations)
        
        public bool HasFlag(AnimationFlags flag) => (Metadata.w & (int)flag) != 0;
        
        public void SetFlag(AnimationFlags flag, bool value)
        {
            if (value)
                Metadata.w |= (int)flag;
            else
                Metadata.w &= ~(int)flag;
        }
        
        public bool IsPlaying => HasFlag(AnimationFlags.Playing);
        public bool IsPaused => HasFlag(AnimationFlags.Paused);
        public bool IsLooping => HasFlag(AnimationFlags.Looping);
        public bool IsFadingIn => HasFlag(AnimationFlags.FadingIn);
        public bool IsFadingOut => HasFlag(AnimationFlags.FadingOut);
        public bool IsInitialized => HasFlag(AnimationFlags.Initialized);
        public bool IsDirty => HasFlag(AnimationFlags.Dirty);
        
        #endregion
        
        #region Property Accessors
        
        public float CurrentTime
        {
            get => TimeData.x;
            set => TimeData.x = math.max(0, value);
        }
        
        public float Length => TimeData.y;
        
        public float Speed
        {
            get => TimeData.z;
            set => TimeData.z = math.max(0, value);
        }
        
        public float NormalizedTime
        {
            get => TimeData.w;
            set => TimeData.w = math.saturate(value);
        }
        
        public float Weight
        {
            get => WeightData.x;
            set => WeightData.x = math.saturate(value);
        }
        
        public float TargetWeight
        {
            get => WeightData.y;
            set => WeightData.y = math.saturate(value);
        }
        
        public float BlendSpeed
        {
            get => WeightData.z;
            set => WeightData.z = math.max(0, value);
        }
        
        public int ClipID => Metadata.x;
        public int Version => Metadata.y;
        public int LoopCount => Metadata.z;
        public int PlayableIndex => References.x;
        public int MaxLoops
        {
            get => References.y;
            set => References.y = value;
        }
        
        #endregion
        
        /// <summary>
        /// Prepare for fade in
        /// </summary>
        public void StartFadeIn(float duration)
        {
            WeightData.x = 0f;
            WeightData.y = 1f;
            WeightData.z = duration > 0 ? 1f / duration : float.MaxValue;
            SetFlag(AnimationFlags.FadingIn, true);
            SetFlag(AnimationFlags.FadingOut, false);
        }
        
        /// <summary>
        /// Prepare for fade out
        /// </summary>
        public void StartFadeOut(float duration)
        {
            WeightData.y = 0f;
            WeightData.z = duration > 0 ? 1f / duration : float.MaxValue;
            SetFlag(AnimationFlags.FadingOut, true);
            SetFlag(AnimationFlags.FadingIn, false);
        }
        
        /// <summary>
        /// Check if state is valid
        /// </summary>
        public bool IsValid => IsInitialized && Metadata.x != 0;
    }
    
    /// <summary>
    /// Batch processing helper for SIMD operations
    /// </summary>
    internal static class AnimationStateBatch
    {
        /// <summary>
        /// Update multiple states in batch (SIMD optimized)
        /// </summary>
        public static void BatchUpdateTime(ref AnimationState[] states, int count, float deltaTime)
        {
            // Process 4 states at once with SIMD
            int batchCount = count / 4;
            
            for (int batch = 0; batch < batchCount; batch++)
            {
                int i = batch * 4;
                
                // Load time data for 4 animations
                float4 times = new float4(
                    states[i].TimeData.x,
                    states[i + 1].TimeData.x,
                    states[i + 2].TimeData.x,
                    states[i + 3].TimeData.x
                );
                
                float4 speeds = new float4(
                    states[i].TimeData.z,
                    states[i + 1].TimeData.z,
                    states[i + 2].TimeData.z,
                    states[i + 3].TimeData.z
                );
                
                // SIMD multiply-add
                times = math.mad(speeds, deltaTime, times);
                
                // Write back
                states[i].TimeData.x = times.x;
                states[i + 1].TimeData.x = times.y;
                states[i + 2].TimeData.x = times.z;
                states[i + 3].TimeData.x = times.w;
            }
            
            // Handle remaining states
            for (int i = batchCount * 4; i < count; i++)
            {
                states[i].UpdateTime(deltaTime);
            }
        }
        
        /// <summary>
        /// Update weights in batch (SIMD optimized)
        /// </summary>
        public static void BatchUpdateWeights(ref AnimationState[] states, int count, float deltaTime)
        {
            for (int i = 0; i < count; i += 4)
            {
                int remaining = math.min(4, count - i);
                
                // Process up to 4 weights at once
                for (int j = 0; j < remaining; j++)
                {
                    states[i + j].UpdateWeight(deltaTime);
                }
            }
        }
    }
}