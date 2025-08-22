using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// SIMD-friendly animation state structure - FIXED VERSION
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
            WeightData = new float4(1f, 1f, AnimationConstants.DEFAULT_BLEND_SPEED, 0f);
            // FIX #1: Initialize loop count to 0, not included in initial play
            Metadata = new int4(clipID, version, 0, (int)AnimationFlags.Initialized);
            References = new int4(playableIndex, -1, -1, 0);
            
            Playable = default(AnimationClipPlayable);
            OnComplete = null;
            OnLoop = null;
        }
        
        /// <summary>
        /// Reset state for reuse
        /// </summary>
        public void Reset()
        {
            TimeData = float4.zero;
            WeightData = float4.zero;
            Metadata = int4.zero;
            References = new int4(-1, -1, -1, 0);
            
            Playable = default(AnimationClipPlayable);
            OnComplete = null;
            OnLoop = null;
        }
        
        /// <summary>
        /// Update weight with blending - FIXED
        /// </summary>
        public void UpdateWeight(float deltaTime)
        {
            if (!IsFadingIn && !IsFadingOut)
                return;
                
            // Calculate weight change
            float delta = WeightData.y - WeightData.x;
            float maxDelta = WeightData.z * deltaTime;
            
            // Apply weight change
            if (math.abs(delta) <= maxDelta)
            {
                // Reached target
                WeightData.x = WeightData.y;
                // FIX #5: Clear fade flags properly
                if (IsFadingIn)
                    SetFlag(AnimationFlags.FadingIn, false);
                if (IsFadingOut)
                    SetFlag(AnimationFlags.FadingOut, false);
            }
            else
            {
                // Move towards target
                WeightData.x += math.sign(delta) * maxDelta;
            }
            
            // FIX #5: Ensure weight is always clamped to [0,1]
            WeightData.x = math.saturate(WeightData.x);
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
        
        public int LoopCount
        {
            get => Metadata.z;
            set => Metadata.z = value;
        }
        
        public int PlayableIndex
        {
            get => References.x;
            set => References.x = value;
        }
        
        public int MaxLoops
        {
            get => References.y;
            set => References.y = value;
        }
        
        #endregion
        
        /// <summary>
        /// Prepare for fade in - FIXED
        /// </summary>
        public void StartFadeIn(float duration)
        {
            WeightData.x = 0f;  // Start from 0
            WeightData.y = 1f;  // Target weight
            WeightData.z = duration > 0 ? 1f / duration : float.MaxValue;
            SetFlag(AnimationFlags.FadingIn, true);
            SetFlag(AnimationFlags.FadingOut, false);
        }
        
        /// <summary>
        /// Prepare for fade out - FIXED
        /// </summary>
        public void StartFadeOut(float duration)
        {
            WeightData.y = 0f;  // Target weight
            WeightData.z = duration > 0 ? 1f / duration : float.MaxValue;
            SetFlag(AnimationFlags.FadingOut, true);
            SetFlag(AnimationFlags.FadingIn, false);
        }
        
        /// <summary>
        /// Check if state is valid
        /// </summary>
        public bool IsValid => Metadata.x != 0;  // Has clip ID
    }
    
    /// <summary>
    /// Batch processing helper for SIMD operations
    /// </summary>
    internal static class AnimationStateBatch
    {
        /// <summary>
        /// Update multiple states in batch (SIMD optimized)
        /// Note: Time update is now handled in the main controller for better loop control
        /// </summary>
        public static void BatchUpdateWeights(ref AnimationState[] states, int count, float deltaTime)
        {
            // Process 4 states at once with SIMD
            int batchCount = count / 4;
            
            for (int batch = 0; batch < batchCount; batch++)
            {
                int i = batch * 4;
                
                // Load weight data for 4 animations
                float4 weights = new float4(
                    states[i].WeightData.x,
                    states[i + 1].WeightData.x,
                    states[i + 2].WeightData.x,
                    states[i + 3].WeightData.x
                );
                
                float4 targets = new float4(
                    states[i].WeightData.y,
                    states[i + 1].WeightData.y,
                    states[i + 2].WeightData.y,
                    states[i + 3].WeightData.y
                );
                
                float4 speeds = new float4(
                    states[i].WeightData.z,
                    states[i + 1].WeightData.z,
                    states[i + 2].WeightData.z,
                    states[i + 3].WeightData.z
                );
                
                // Calculate weight changes
                float4 delta = targets - weights;
                float4 maxDelta = speeds * deltaTime;
                float4 absDelta = math.abs(delta);
                float4 sign = math.sign(delta);
                
                // Apply changes
                float4 change = math.min(absDelta, maxDelta) * sign;
                weights = math.saturate(weights + change);
                
                // Write back
                states[i].WeightData.x = weights.x;
                states[i + 1].WeightData.x = weights.y;
                states[i + 2].WeightData.x = weights.z;
                states[i + 3].WeightData.x = weights.w;
            }
            
            // Handle remaining states
            for (int i = batchCount * 4; i < count; i++)
            {
                states[i].UpdateWeight(deltaTime);
            }
        }
    }
}