using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// Playback operations for PlayableAnimationController - COMPLETE FIX
    /// All issues resolved
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
        /// Play animation with specific mode - COMPLETELY FIXED
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
                    // FIX #10: Queue with proper data
                    if (ActiveAnimationCount > 0)
                    {
                        var queuedAnim = new QueuedAnimation
                        {
                            ClipID = clipID,
                            OnComplete = onComplete,
                            Version = NextVersion()
                        };
                        animationQueue.Enqueue(queuedAnim);
                        
                        DebugLog($"Queued animation: {clip.name}");
                        // Return a queue handle (not ideal but works)
                        return new AnimationHandle(
                            AnimationConstants.INVALID_SLOT,
                            queuedAnim.Version,
                            clipID,
                            this
                        );
                    }
                    break;
                    
                case PlayMode.Additive:
                    // Play alongside current animations
                    break;
            }
            
            // FIX #3: Get playable directly from pool
            var playable = playablePool.Rent(clip);
            if (!playable.IsValid())
            {
                // Pool failed, create new
                playable = AnimationClipPlayable.Create(playableGraph, clip);
            }
            
            if (!playable.IsValid())
                return AnimationHandle.Invalid;
            
            // Get free slot
            int slot = GetFreeSlot();
            if (slot < 0)
                return AnimationHandle.Invalid;
            
            // Initialize state
            ref var state = ref states[slot];
            state.Initialize(clip, clipID, NextVersion(), slot);
            state.OnComplete = onComplete;
            state.SetFlag(AnimationFlags.Playing, true);
            
            if (autoStopOnComplete)
                state.SetFlag(AnimationFlags.AutoStop, true);
            
            // Connect to mixer
            ConnectToMixer(ref state, playable, clip);
            
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
                if (slotInUse[i] && states[i].IsPlaying && !states[i].IsFadingOut)
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
                // FIX #8: Set initial weight to 0 for fade in
                state.Weight = 0f;
                mixerPlayable.SetInputWeight(state.PlayableIndex, 0f);
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
                
                // Set proper duration based on loop count
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
            if (slot < 0 || slot >= AnimationConstants.MAX_SLOTS || !slotInUse[slot])
                return;
                
            ref var state = ref states[slot];
            if (!state.IsPlaying && !state.IsInitialized)
                return;
            
            // Get clip name for events
            string clipName = null;
            if (clipIDToClip.TryGetValue(state.ClipID, out var clip))
            {
                clipName = clip.name;
            }
            
            // Fire completion callback if not interrupted and the animation hasn’t already finished.
            // This prevents OnComplete from firing twice when auto-stop is enabled.
            if (!wasInterrupted && state.OnComplete != null && state.IsPlaying)
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
            
            DebugLog($"Stopped animation in slot {slot}");
            
            // Process queue
            ProcessQueue();
        }
        
        private void StopAllInternal(bool clearQueue)
        {
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (slotInUse[i] && states[i].IsPlaying)
                {
                    StopInternal(i, true);
                }
            }
            
            if (clearQueue)
            {
                animationQueue.Clear();
            }
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
                if (slotInUse[i] && states[i].IsPlaying && !states[i].IsPaused && states[i].Playable.IsValid())
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
                if (slotInUse[i] && states[i].IsPlaying && states[i].IsPaused && states[i].Playable.IsValid())
                {
                    states[i].SetFlag(AnimationFlags.Paused, false);
                    states[i].Playable.SetSpeed(states[i].Speed * globalSpeed);
                }
            }
            DebugLog("Resumed all animations");
        }
        
        #endregion
        
        #region Playable Management - COMPLETELY FIXED
        
        /// <summary>
        /// Connect playable to mixer - FIXED #4 & others
        /// </summary>
        private void ConnectToMixer(ref AnimationState state, AnimationClipPlayable playable, AnimationClip clip)
        {
            int mixerIndex = state.PlayableIndex;
            
            // Disconnect current input if exists
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
            
            // Consistent speed calculation
            playable.SetSpeed(state.Speed * globalSpeed);
            playable.SetTime(0);
            playable.SetDone(false);
            
            // FIX #4: Use clip.length directly for duration
            if (state.IsLooping)
            {
                if (state.MaxLoops > 0)
                {
                    // Set duration based on loop count
                    playable.SetDuration(clip.length * state.MaxLoops);
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
                playable.SetDuration(clip.length);
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
        
        #region Queue Processing - COMPLETELY FIXED
        
        private void ProcessQueue()
        {
            // FIX #5: Better queue processing
            if (animationQueue.Count == 0)
                return;
                
            // Check if we can play next queued animation
            bool canProcess = true;
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (!slotInUse[i]) continue;
                
                ref var state = ref states[i];
                // Check if actually playing (not just marked as playing)
                if (state.IsPlaying && !state.IsFadingOut && state.Playable.IsValid())
                {
                    // Additional check: is it actually progressing?
                    if (state.CurrentTime < state.Length - 0.01f || state.IsLooping)
                    {
                        canProcess = false;
                        break;
                    }
                }
            }
            
            if (!canProcess)
                return;
                
            // Get next queued animation
            var queued = animationQueue.Dequeue();
            
            // Find the clip and play it
            if (clipIDToClip.TryGetValue(queued.ClipID, out var clip))
            {
                Play(clip, queued.OnComplete);
            }
        }
        
        #endregion
        
        #region Update Loop - COMPLETELY FIXED
        
        private void UpdateAnimations(float deltaTime)
        {
            if (ActiveAnimationCount == 0)
                return;
            
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (!slotInUse[i])
                    continue;
                    
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
            }
        }
        
        /// <summary>
        /// Update animation time and handle looping - COMPLETELY FIXED
        /// </summary>
        private bool UpdateAnimationTime(ref AnimationState state, float deltaTime)
        {
            if (!state.IsPlaying || state.IsPaused)
                return false;
                
            // Update time
            float previousTime = state.CurrentTime;
            float deltaProgress = deltaTime * state.Speed * globalSpeed;
            state.CurrentTime += deltaProgress;
            
            // Check if we've completed
            if (state.CurrentTime >= state.Length)
            {
                if (state.IsLooping)
                {
                    // FIX #7: Process loops one at a time
                    while (state.CurrentTime >= state.Length)
                    {
                        state.CurrentTime -= state.Length;
                        state.LoopCount++;
                        
                        // Fire loop event
                        if (clipIDToClip.TryGetValue(state.ClipID, out var clip))
                        {
                            FireAnimationLoop(clip.name, state.LoopCount);
                        }
                        
                        // Invoke loop callback
                        state.OnLoop?.Invoke();
                        
                        // Check max loops
                        if (state.MaxLoops > 0 && state.LoopCount >= state.MaxLoops)
                        {
                            // Clamp to end of last loop
                            state.CurrentTime = state.Length;
                            state.SetFlag(AnimationFlags.Playing, false);
                            state.OnComplete?.Invoke();
                            return true;
                        }
                        
                        // Safety check for infinite loop
                        if (deltaProgress > state.Length * 10)
                        {
                            DebugLog("Warning: Extremely large delta time detected, clamping loops");
                            state.CurrentTime = 0;
                            break;
                        }
                    }
                    
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
