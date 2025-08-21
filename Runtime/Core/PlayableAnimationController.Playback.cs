using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// Playback operations for PlayableAnimationController
    /// Handles play, stop, pause, and queue operations
    /// </summary>
    public partial class PlayableAnimationController
    {
        #region Play Operations
        
        /// <summary>
        /// Play animation clip (stops current animations)
        /// </summary>
        public AnimationHandle Play(AnimationClip clip, Action onComplete = null)
        {
            return PlayWithMode(clip, PlayMode.Single, onComplete);
        }
        
        /// <summary>
        /// Play animation with specific mode
        /// </summary>
        public AnimationHandle PlayWithMode(AnimationClip clip, PlayMode mode, Action onComplete = null)
        {
            #if DEBUG
            Assert(clip != null, "Animation clip cannot be null");
            Assert(graphInitialized, "Graph not initialized");
            #endif
            
            if (clip == null || !graphInitialized)
                return AnimationHandle.Invalid;
            
            // Resume graph if paused
            ResumeGraph();
            
            int clipID = clip.GetInstanceID();
            
            // Store clip reference
            if (!clipIDToClip.ContainsKey(clipID))
            {
                clipIDToClip[clipID] = clip;
            }
            
            // Handle play mode
            switch (mode)
            {
                case PlayMode.Single:
                    // Stop all current animations
                    StopAllInternal(false);
                    break;
                    
                case PlayMode.Queue:
                    // Queue if something is playing
                    if (activeStateCount > 0)
                    {
                        var queueHandle = new AnimationHandle(
                            AnimationConstants.INVALID_SLOT,
                            nextVersion++,
                            clipID,
                            this
                        );
                        animationQueue.Enqueue(queueHandle);
                        DebugLog($"Queued animation: {clip.name}");
                        return queueHandle;
                    }
                    break;
                    
                case PlayMode.Additive:
                    // Play alongside current animations
                    break;
            }
            
            // Get or create playable
            var playable = GetOrCreatePlayable(clip);
            if (!playable.IsValid())
                return AnimationHandle.Invalid;
            
            // Get slot
            int slot = GetOrAssignSlot(clipID);
            if (slot < 0)
                return AnimationHandle.Invalid;
            
            // Initialize state
            ref var state = ref states[slot];
            state.Initialize(clip, clipID, nextVersion++, slot);
            state.OnComplete = onComplete;
            state.SetFlag(AnimationFlags.Playing, true);
            
            if (autoStopOnComplete)
                state.SetFlag(AnimationFlags.AutoStop, true);
            
            // Connect to mixer
            ConnectToMixer(ref state, playable);
            
            // Update active count
            activeStateCount++;
            
            var handle = new AnimationHandle(slot, state.Version, clipID, this);
            DebugLog($"Playing animation: {clip.name} (Slot: {slot})");
            
            // Fire start event
            FireAnimationStart(clip.name);
            
            return handle;
        }
        
        /// <summary>
        /// Play animation with crossfade
        /// </summary>
        public AnimationHandle PlayWithCrossfade(AnimationClip clip, float fadeTime = 0.3f, Action onComplete = null)
        {
            #if DEBUG
            Assert(clip != null, "Animation clip cannot be null");
            Assert(fadeTime >= 0, "Fade time must be non-negative");
            #endif
            
            if (clip == null || !graphInitialized)
                return AnimationHandle.Invalid;
            
            // Fade out current animations
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying && !states[i].IsFadingOut)
                {
                    states[i].StartFadeOut(fadeTime);
                }
            }
            
            // Play new animation with fade in
            var handle = PlayWithMode(clip, PlayMode.Additive, onComplete);
            if (handle.IsValid)
            {
                ref var state = ref states[handle.SlotIndex];
                state.StartFadeIn(fadeTime);
            }
            
            return handle;
        }
        
        /// <summary>
        /// Play animation with looping
        /// </summary>
        public AnimationHandle PlayLooped(AnimationClip clip, int loopCount = -1, Action onComplete = null)
        {
            var handle = Play(clip, onComplete);
            if (handle.IsValid)
            {
                ref var state = ref states[handle.SlotIndex];
                state.SetFlag(AnimationFlags.Looping, true);
                state.MaxLoops = loopCount;
            }
            return handle;
        }
        
        #endregion
        
        #region Stop Operations
        
        /// <summary>
        /// Stop specific animation by handle
        /// </summary>
        public void Stop(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return;
                
            StopInternal(handle.SlotIndex, false);
        }
        
        /// <summary>
        /// Stop all animations
        /// </summary>
        public void StopAll()
        {
            StopAllInternal(true);
        }
        
        private void StopInternal(int slot, bool wasInterrupted)
        {
            if (slot < 0 || slot >= AnimationConstants.MAX_SLOTS)
                return;
                
            ref var state = ref states[slot];
            if (!state.IsPlaying)
                return;
            
            // Get clip name for events
            string clipName = null;
            if (clipIDToClip.TryGetValue(state.ClipID, out var clip))
            {
                clipName = clip.name;
            }
            
            // Fire completion callback if not interrupted
            if (!wasInterrupted && state.OnComplete != null)
            {
                state.OnComplete.Invoke();
            }
            
            // Fire interrupted event if applicable
            if (wasInterrupted && clipName != null)
            {
                FireAnimationInterrupted(clipName);
            }
            else if (clipName != null)
            {
                FireAnimationEnd(clipName);
            }
            
            // Disconnect from mixer
            if (state.PlayableIndex >= 0)
            {
                mixerPlayable.SetInputWeight(state.PlayableIndex, 0f);
            }
            
            // Clear state
            state.SetFlag(AnimationFlags.Playing, false);
            state.OnComplete = null;
            state.OnLoop = null;
            
            // Update active count
            activeStateCount = math.max(0, activeStateCount - 1);
            
            // Free slot
            FreeSlot(slot);
            
            DebugLog($"Stopped animation in slot {slot}");
            
            // Process queue
            ProcessQueue();
        }
        
        private void StopAllInternal(bool clearQueue)
        {
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying)
                {
                    StopInternal(i, true);
                }
            }
            
            if (clearQueue)
            {
                animationQueue.Clear();
            }
            
            activeStateCount = 0;
        }
        
        #endregion
        
        #region Pause/Resume Operations
        
        /// <summary>
        /// Pause animation by handle
        /// </summary>
        public void Pause(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            if (state.IsPlaying && !state.IsPaused)
            {
                state.SetFlag(AnimationFlags.Paused, true);
                state.Playable.SetSpeed(0f);
                DebugLog($"Paused animation in slot {handle.SlotIndex}");
            }
        }
        
        /// <summary>
        /// Resume animation by handle
        /// </summary>
        public void Resume(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            if (state.IsPlaying && state.IsPaused)
            {
                state.SetFlag(AnimationFlags.Paused, false);
                state.Playable.SetSpeed(state.Speed * globalSpeed);
                DebugLog($"Resumed animation in slot {handle.SlotIndex}");
            }
        }
        
        /// <summary>
        /// Pause all animations
        /// </summary>
        public void PauseAll()
        {
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying && !states[i].IsPaused)
                {
                    states[i].SetFlag(AnimationFlags.Paused, true);
                    states[i].Playable.SetSpeed(0f);
                }
            }
            DebugLog("Paused all animations");
        }
        
        /// <summary>
        /// Resume all animations
        /// </summary>
        public void ResumeAll()
        {
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying && states[i].IsPaused)
                {
                    states[i].SetFlag(AnimationFlags.Paused, false);
                    states[i].Playable.SetSpeed(states[i].Speed * globalSpeed);
                }
            }
            DebugLog("Resumed all animations");
        }
        
        #endregion
        
        #region Playable Management
        
        /// <summary>
        /// Get or create playable for clip
        /// </summary>
        private AnimationClipPlayable GetOrCreatePlayable(AnimationClip clip)
        {
            int clipID = clip.GetInstanceID();
            
            // Check if already exists
            if (clipIDToPlayable.TryGetValue(clipID, out var existing))
            {
                if (existing.IsValid())
                {
                    existing.SetTime(0);
                    existing.SetDone(false);
                    return existing;
                }
            }
            
            // Try to get from pool
            var pooled = playablePool.Rent(clip);
            if (pooled.IsValid())
            {
                clipIDToPlayable[clipID] = pooled;
                return pooled;
            }
            
            // Create new playable
            var playable = AnimationClipPlayable.Create(playableGraph, clip);
            clipIDToPlayable[clipID] = playable;
            
            DebugLog($"Created new playable for clip: {clip.name}");
            return playable;
        }
        
        /// <summary>
        /// Connect playable to mixer
        /// </summary>
        private void ConnectToMixer(ref AnimationState state, AnimationClipPlayable playable)
        {
            int mixerIndex = state.PlayableIndex;
    
            // Clear existing connection
            if (mixerPlayable.GetInput(mixerIndex).IsValid())
            {
                playableGraph.Disconnect(mixerPlayable, mixerIndex);
                mixerPlayable.SetInputWeight(mixerIndex, 0f);
            }
    
            // Connect playable
            playableGraph.Connect(playable, 0, mixerPlayable, mixerIndex);
    
            // CRITICAL: Set weight to 1.0f initially, not state.Weight
            mixerPlayable.SetInputWeight(mixerIndex, 1.0f);  // ← This is crucial
    
            // Store reference
            state.Playable = playable;
            state.Weight = 1.0f;  // Ensure state weight is also set
    
            // Set properties
            playable.SetSpeed(state.Speed * globalSpeed);
            playable.SetTime(0);
    
            Debug.Log($"Connected playable to slot {mixerIndex} with weight 1.0f");
        }

        
        /// <summary>
        /// Prewarm clip for faster first play
        /// </summary>
        public void PrewarmClip(AnimationClip clip)
        {
            if (clip == null || !graphInitialized)
                return;
                
            int clipID = clip.GetInstanceID();
            if (!clipIDToPlayable.ContainsKey(clipID))
            {
                var playable = AnimationClipPlayable.Create(playableGraph, clip);
                clipIDToPlayable[clipID] = playable;
                playablePool.Prewarm(clip, 2); // Pre-create 2 instances
                DebugLog($"Prewarmed clip: {clip.name}");
            }
        }
        
        #endregion
        
        #region Queue Processing
        
        private void ProcessQueue()
        {
            if (animationQueue.Count == 0 || activeStateCount > 0)
                return;
                
            // Get next queued animation
            var queued = animationQueue.Dequeue();
            
            // Find the clip
            AnimationClip clip = null;
            foreach (var kvp in clipIDToPlayable)
            {
                if (kvp.Key == queued.ClipID)
                {
                    // This is a workaround - in production you'd maintain clip references
                    // For now we can't get the clip back from just the ID
                    break;
                }
            }
            
            if (clip != null)
            {
                Play(clip);
            }
        }
        
        #endregion
        
        #region Update Loop
        
        private void UpdateAnimations(float deltaTime)
        {
            if (activeStateCount == 0)
                return;
            
            // Batch update using SIMD
            AnimationStateBatch.BatchUpdateTime(ref states, AnimationConstants.MAX_SLOTS, deltaTime);
            AnimationStateBatch.BatchUpdateWeights(ref states, AnimationConstants.MAX_SLOTS, deltaTime);
            
            // Apply weights to mixer and check completion
            int stillActive = 0;
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                ref var state = ref states[i];
                
                if (!state.IsPlaying)
                    continue;
                
                // Update mixer weight if changed
                if (state.IsDirty || state.IsFadingIn || state.IsFadingOut)
                {
                    mixerPlayable.SetInputWeight(state.PlayableIndex, state.Weight);
                    state.SetFlag(AnimationFlags.Dirty, false);
                }
                
                // Check for completion
                if (!state.IsPlaying && state.HasFlag(AnimationFlags.AutoStop))
                {
                    StopInternal(i, false);
                }
                else if (state.IsPlaying)
                {
                    stillActive++;
                }
                
                // Check for fade out completion
                if (state.IsFadingOut && state.Weight <= AnimationConstants.MIN_WEIGHT_THRESHOLD)
                {
                    StopInternal(i, true);
                }
            }
            
            activeStateCount = stillActive;
        }
        
        #endregion
    }
}