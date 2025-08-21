using System;
using Unity.Mathematics;

namespace LightningAnimation
{
    /// <summary>
    /// Lightweight handle for referencing and manipulating animations without lookups
    /// Only 12 bytes, passed by value for zero allocations
    /// </summary>
    [Serializable]
    public readonly struct AnimationHandle : IEquatable<AnimationHandle>
    {
        // Packed data for efficiency
        public readonly int SlotIndex;      // Which slot in the controller (4 bytes)
        public readonly int Version;        // Generation counter to detect stale handles (4 bytes)
        public readonly int ClipID;         // Animation clip instance ID (4 bytes)
        
        // Cached controller reference for direct manipulation (not serialized)
        [NonSerialized]
        internal readonly PlayableAnimationController Controller;
        
        public AnimationHandle(int slotIndex, int version, int clipID, PlayableAnimationController controller = null)
        {
            SlotIndex = slotIndex;
            Version = version;
            ClipID = clipID;
            Controller = controller;
        }
        
        /// <summary>
        /// Check if this handle is valid (slot in range and version matches)
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (SlotIndex < 0 || SlotIndex >= AnimationConstants.MAX_SLOTS)
                    return false;
                    
                if (Controller != null)
                    return Controller.IsHandleValid(this);
                    
                return Version != AnimationConstants.INVALID_VERSION;
            }
        }
        
        /// <summary>
        /// Invalid handle singleton
        /// </summary>
        public static readonly AnimationHandle Invalid = new AnimationHandle(
            AnimationConstants.INVALID_SLOT,
            AnimationConstants.INVALID_VERSION,
            0,
            null
        );
        
        #region Equality
        
        public bool Equals(AnimationHandle other)
        {
            return SlotIndex == other.SlotIndex && 
                   Version == other.Version && 
                   ClipID == other.ClipID;
        }
        
        public override bool Equals(object obj)
        {
            return obj is AnimationHandle handle && Equals(handle);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(SlotIndex, Version, ClipID);
        }
        
        public static bool operator ==(AnimationHandle left, AnimationHandle right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(AnimationHandle left, AnimationHandle right)
        {
            return !left.Equals(right);
        }
        
        #endregion
        
        #region Fluent API (Optional convenience methods)
        
        /// <summary>
        /// Set animation speed (fluent API)
        /// </summary>
        public AnimationHandle SetSpeed(float speed)
        {
            Controller?.SetSpeed(this, speed);
            return this;
        }
        
        /// <summary>
        /// Set animation weight (fluent API)
        /// </summary>
        public AnimationHandle SetWeight(float weight)
        {
            Controller?.SetWeight(this, weight);
            return this;
        }
        
        /// <summary>
        /// Set looping state (fluent API)
        /// </summary>
        public AnimationHandle SetLooping(bool loop, int maxLoops = -1)
        {
            Controller?.SetLooping(this, loop, maxLoops);
            return this;
        }
        
        /// <summary>
        /// Stop this animation
        /// </summary>
        public void Stop()
        {
            Controller?.Stop(this);
        }
        
        /// <summary>
        /// Pause this animation
        /// </summary>
        public void Pause()
        {
            Controller?.Pause(this);
        }
        
        /// <summary>
        /// Resume this animation
        /// </summary>
        public void Resume()
        {
            Controller?.Resume(this);
        }
        
        #endregion
        
        #region Property Accessors
        
        /// <summary>
        /// Get/Set animation speed
        /// </summary>
        public float Speed
        {
            get => Controller?.GetSpeed(this) ?? 0f;
            set => Controller?.SetSpeed(this, value);
        }
        
        /// <summary>
        /// Get/Set animation weight
        /// </summary>
        public float Weight
        {
            get => Controller?.GetWeight(this) ?? 0f;
            set => Controller?.SetWeight(this, value);
        }
        
        /// <summary>
        /// Get normalized progress (0-1)
        /// </summary>
        public float Progress
        {
            get => Controller?.GetProgress(this) ?? 0f;
            set => Controller?.SetProgress(this, value);
        }
        
        /// <summary>
        /// Get current time in seconds
        /// </summary>
        public float Time
        {
            get => Controller?.GetTime(this) ?? 0f;
            set => Controller?.SetTime(this, value);
        }
        
        /// <summary>
        /// Check if animation is playing
        /// </summary>
        public bool IsPlaying => Controller?.IsPlaying(this) ?? false;
        
        /// <summary>
        /// Check if animation is paused
        /// </summary>
        public bool IsPaused => Controller?.IsPaused(this) ?? false;
        
        #endregion
        
        public override string ToString()
        {
            return IsValid ? 
                $"AnimationHandle[Slot:{SlotIndex}, Ver:{Version}, Clip:{ClipID}]" : 
                "AnimationHandle[Invalid]";
        }
    }
}