using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// Playback operations for PlayableAnimationController - FIXED VERSION
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
        /// Play animation with specific mode - FIXED
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
                        
                        // Store the callback for when it plays
                        // Note: This is simplified - in production you'd need a queue for callbacks too
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
            
            // Get free slot
            int slot = GetFreeSlot();
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
        /// Play animation with crossfade - FIXED
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
                // Apply initial weight
                mixerPlayable.SetInputWeight(state.PlayableIndex, state.Weight);
            }
            
            return handle;
        }
        
        /// <summary>
        /// Play animation with looping - FIXED
        /// </summary>
        public AnimationHandle PlayLooped(AnimationClip clip, int loopCount = -1, Action onComplete = null)
        {
            var handle = Play(clip, onComplete);
            if (handle.IsValid)
            {
                ref var state = ref states[handle.SlotIndex];
                state.SetFlag(AnimationFlags.Looping, true);
                state.MaxLoops = loopCount;
                
                // FIX #1: Set proper duration based on loop count
                if (state.Playable.IsValid())
                {
                    if (loopCount > 0)
                    {
                        // Set duration to exact loop count * clip length
                        state.Playable.SetDuration(clip.length * loopCount);
                    }
                    else
                    {
                        // Infinite looping
                        state.Playable.SetDuration(double.MaxValue);
                    }
                }
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
            
            // Clear state and free slot
            FreeSlot(slot);
            
            // Update active count
            activeStateCount = math.max(0, activeStateCount - 1);
            
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
                if (state.Playable.IsValid())
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
                if (state.Playable.IsValid())
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
                if (states[i].IsPlaying && !states[i].IsPaused && states[i].Playable.IsValid())
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
                if (states[i].IsPlaying && states[i].IsPaused && states[i].Playable.IsValid())
                {
                    states[i].SetFlag(AnimationFlags.Paused, false);
                    states[i].Playable.SetSpeed(states[i].Speed * globalSpeed);
                }
            }
            DebugLog("Resumed all animations");
        }
        
        #endregion
        
        #region Playable Management - FIXED
        
        /// <summary>
        /// Get or create playable for clip - IMPROVED
        /// </summary>
        private AnimationClipPlayable GetOrCreatePlayable(AnimationClip clip)
        {
            int clipID = clip.GetInstanceID();
            
            // Try to get from pool first
            var pooled = playablePool.Rent(clip);
            if (pooled.IsValid())
            {
                clipIDToPlayable[clipID] = pooled;
                return pooled;
            }
            
            // Create new playable if pool failed
            var playable = AnimationClipPlayable.Create(playableGraph, clip);
            clipIDToPlayable[clipID] = playable;
            
            DebugLog($"Created new playable for clip: {clip.name}");
            return playable;
        }
        
        /// <summary>
        /// Connect playable to mixer - FIXED
        /// </summary>
        private void ConnectToMixer(ref AnimationState state, AnimationClipPlayable playable)
        {
            int mixerIndex = state.PlayableIndex;
            
            // FIX #3: Proper disconnection and cleanup
            var currentInput = mixerPlayable.GetInput(mixerIndex);
            if (currentInput.IsValid())
            {
                playableGraph.Disconnect(mixerPlayable, mixerIndex);
            }
            
            // Connect playable
            playableGraph.Connect(playable, 0, mixerPlayable, mixerIndex);
            
            // Set initial weight based on whether we're fading in
            float initialWeight = state.IsFadingIn ? 0f : 1f;
            mixerPlayable.SetInputWeight(mixerIndex, initialWeight);
            
            // Store reference
            state.Playable = playable;
            state.Weight = initialWeight;
            
            // FIX #7: Consistent speed calculation
            playable.SetSpeed(state.Speed * globalSpeed);
            playable.SetTime(0);
            playable.SetDone(false);
            
            // FIX #1: Proper duration for looping
            if (state.IsLooping)
            {
                if (state.MaxLoops > 0)
                {
                    // Set duration based on loop count
                    playable.SetDuration(state.Length * state.MaxLoops);
                }
                else
                {
                    // Infinite looping
                    playable.SetDuration(double.MaxValue);
                }
            }
            else
            {
                // Single play
                playable.SetDuration(state.Length);
            }
            
            DebugLog($"Connected playable to slot {mixerIndex} with weight {initialWeight:F2}");
        }
        
        /// <summary>
        /// Prewarm clip for faster first play
        /// </summary>
        public void PrewarmClip(AnimationClip clip)
        {
            if (clip == null || !graphInitialized)
                return;
                
            int clipID = clip.GetInstanceID();
            clipIDToClip[clipID] = clip;
            playablePool.Prewarm(clip, 2); // Pre-create 2 instances
            DebugLog($"Prewarmed clip: {clip.name}");
        }
        
        #endregion
        
        #region Queue Processing
        
        private void ProcessQueue()
        {
            // FIX #2: Better queue processing logic
            if (animationQueue.Count == 0)
                return;
                
            // Check if we can play next queued animation
            // Only process if no primary animations are playing (ignore fading out ones)
            bool canProcess = true;
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying && !states[i].IsFadingOut)
                {
                    canProcess = false;
                    break;
                }
            }
            
            if (!canProcess)
                return;
                
            // Get next queued animation
            var queued = animationQueue.Dequeue();
            
            // Find the clip and play it
            if (clipIDToClip.TryGetValue(queued.ClipID, out var clip))
            {
                Play(clip);
            }
        }
        
        #endregion
        
        #region Update Loop - FIXED
        
        private void UpdateAnimations(float deltaTime)
        {
            if (activeStateCount == 0)
                return;
            
            int stillActive = 0;
            
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                ref var state = ref states[i];
                
                if (!state.IsPlaying)
                    continue;
                
                // Update time (handles looping internally)
                bool completed = UpdateAnimationTime(ref state, deltaTime);
                
                // Update weight if fading
                if (state.IsFadingIn || state.IsFadingOut)
                {
                    state.UpdateWeight(deltaTime);
                    if (state.Playable.IsValid())
                    {
                        mixerPlayable.SetInputWeight(state.PlayableIndex, state.Weight);
                    }
                    
                    // Check for fade out completion
                    if (state.IsFadingOut && state.Weight <= AnimationConstants.MIN_WEIGHT_THRESHOLD)
                    {
                        StopInternal(i, true);
                        continue;
                    }
                }
                
                // Check for completion
                if (completed && state.HasFlag(AnimationFlags.AutoStop))
                {
                    StopInternal(i, false);
                    continue;
                }
                
                if (state.IsPlaying)
                {
                    stillActive++;
                }
            }
            
            activeStateCount = stillActive;
        }
        
        /// <summary>
        /// Update animation time and handle looping - FIXED
        /// </summary>
        private bool UpdateAnimationTime(ref AnimationState state, float deltaTime)
        {
            if (!state.IsPlaying || state.IsPaused)
                return false;
                
            // Update time
            float previousTime = state.CurrentTime;
            state.CurrentTime += deltaTime * state.Speed * globalSpeed;
            
            // Check if we've completed
            if (state.CurrentTime >= state.Length)
            {
                if (state.IsLooping)
                {
                    // FIX #1: Proper loop counting
                    // Calculate how many times we've looped in this frame
                    int loopsThisFrame = (int)(state.CurrentTime / state.Length);
                    
                    // Update loop count
                    state.LoopCount += loopsThisFrame;
                    
                    // Fire loop event for each loop
                    if (clipIDToClip.TryGetValue(state.ClipID, out var clip))
                    {
                        for (int i = 0; i < loopsThisFrame; i++)
                        {
                            FireAnimationLoop(clip.name, state.LoopCount - loopsThisFrame + i + 1);
                        }
                    }
                    
                    // Invoke loop callback
                    state.OnLoop?.Invoke();
                    
                    // Check max loops (fixed comparison)
                    if (state.MaxLoops > 0 && state.LoopCount >= state.MaxLoops)
                    {
                        // Clamp to end of last loop
                        state.CurrentTime = state.Length;
                        state.SetFlag(AnimationFlags.Playing, false);
                        state.OnComplete?.Invoke();
                        return true;
                    }
                    
                    // Wrap time for next loop
                    state.CurrentTime = math.fmod(state.CurrentTime, state.Length);
                    
                    // Update the playable for smooth looping
                    if (state.Playable.IsValid())
                    {
                        state.Playable.SetTime(state.CurrentTime);
                    }
                    
                    return false;
                }
                else
                {
                    // Non-looping animation completed
                    state.CurrentTime = state.Length;
                    state.SetFlag(AnimationFlags.Playing, false);
                    state.OnComplete?.Invoke();
                    return true;
                }
            }
            
            // Update playable time
            if (state.Playable.IsValid())
            {
                state.Playable.SetTime(state.CurrentTime);
            }
            
            // Update normalized time
            state.NormalizedTime = state.Length > 0 ? state.CurrentTime / state.Length : 0f;
            
            return false;
        }
        
        #endregion
    }
}