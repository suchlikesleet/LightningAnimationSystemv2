using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// State manipulation operations for PlayableAnimationController
    /// Provides direct handle-based access to animation properties
    /// </summary>
    public partial class PlayableAnimationController
    {
        #region Handle Validation
        
        /// <summary>
        /// Check if handle is valid
        /// </summary>
        public bool IsHandleValid(AnimationHandle handle)
        {
            if (handle.SlotIndex < 0 || handle.SlotIndex >= AnimationConstants.MAX_SLOTS)
                return false;
                
            return states[handle.SlotIndex].Version == handle.Version;
        }
        
        /// <summary>
        /// Get animation length
        /// </summary>
        public float GetLength(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0f;
                
            return states[handle.SlotIndex].Length;
        }
        
        #endregion
        
        #region Speed Control
        
        /// <summary>
        /// Set animation speed via handle
        /// </summary>
        public void SetSpeed(AnimationHandle handle, float speed)
        {
            #if DEBUG
            Assert(IsHandleValid(handle), "Invalid animation handle");
            Assert(speed >= 0, "Speed must be non-negative");
            #endif
            
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            state.Speed = math.max(0f, speed);
            
            if (state.IsPlaying && !state.IsPaused)
            {
                state.Playable.SetSpeed(state.Speed * globalSpeed);
            }
            
            state.SetFlag(AnimationFlags.SpeedDirty, true);
        }
        
        /// <summary>
        /// Get animation speed
        /// </summary>
        public float GetSpeed(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0f;
                
            return states[handle.SlotIndex].Speed;
        }
        
        /// <summary>
        /// Batch set speed for multiple animations
        /// </summary>
        public void BatchSetSpeed(AnimationHandle[] handles, float speed)
        {
            speed = math.max(0f, speed);
            
            for (int i = 0; i < handles.Length; i++)
            {
                if (IsHandleValid(handles[i]))
                {
                    ref var state = ref states[handles[i].SlotIndex];
                    state.Speed = speed;
                    
                    if (state.IsPlaying && !state.IsPaused)
                    {
                        state.Playable.SetSpeed(speed * globalSpeed);
                    }
                }
            }
        }
        
        #endregion
        
        #region Weight Control
        
        /// <summary>
        /// Set animation weight via handle
        /// </summary>
        public void SetWeight(AnimationHandle handle, float weight)
        {
            #if DEBUG
            Assert(IsHandleValid(handle), "Invalid animation handle");
            #endif
            
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            state.Weight = math.saturate(weight);
            state.TargetWeight = state.Weight;
            
            if (state.PlayableIndex >= 0)
            {
                mixerPlayable.SetInputWeight(state.PlayableIndex, state.Weight);
            }
            
            state.SetFlag(AnimationFlags.WeightDirty, true);
        }
        
        /// <summary>
        /// Get animation weight
        /// </summary>
        public float GetWeight(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0f;
                
            return states[handle.SlotIndex].Weight;
        }
        
        /// <summary>
        /// Fade weight to target value
        /// </summary>
        public void FadeWeight(AnimationHandle handle, float targetWeight, float duration)
        {
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            state.TargetWeight = math.saturate(targetWeight);
            state.BlendSpeed = duration > 0 ? 1f / duration : float.MaxValue;
            
            if (targetWeight > state.Weight)
                state.SetFlag(AnimationFlags.FadingIn, true);
            else if (targetWeight < state.Weight)
                state.SetFlag(AnimationFlags.FadingOut, true);
        }
        
        #endregion
        
        #region Time Control
        
        /// <summary>
        /// Set animation time via handle
        /// </summary>
        public void SetTime(AnimationHandle handle, float time)
        {
            #if DEBUG
            Assert(IsHandleValid(handle), "Invalid animation handle");
            #endif
            
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            state.CurrentTime = math.max(0f, time);
            
            if (state.Playable.IsValid())
            {
                state.Playable.SetTime(state.CurrentTime);
            }
            
            state.SetFlag(AnimationFlags.TimeDirty, true);
        }
        
        /// <summary>
        /// Get animation time
        /// </summary>
        public float GetTime(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0f;
                
            return states[handle.SlotIndex].CurrentTime;
        }
        
        /// <summary>
        /// Set normalized progress (0-1)
        /// </summary>
        public void SetProgress(AnimationHandle handle, float progress)
        {
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            float time = math.saturate(progress) * state.Length;
            SetTime(handle, time);
        }
        
        /// <summary>
        /// Get normalized progress (0-1)
        /// </summary>
        public float GetProgress(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0f;
                
            var state = states[handle.SlotIndex];
            return state.Length > 0 ? state.CurrentTime / state.Length : 0f;
        }
        
        #endregion
        
        #region Loop Control
        
        /// <summary>
        /// Set looping state
        /// </summary>
        public void SetLooping(AnimationHandle handle, bool loop, int maxLoops = -1)
        {
            if (!IsHandleValid(handle))
                return;
                
            ref var state = ref states[handle.SlotIndex];
            state.SetFlag(AnimationFlags.Looping, loop);
            state.MaxLoops = maxLoops;
        }
        
        /// <summary>
        /// Get current loop count
        /// </summary>
        public int GetLoopCount(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return 0;
                
            return states[handle.SlotIndex].LoopCount;
        }
        
        #endregion
        
        #region State Queries
        
        /// <summary>
        /// Check if animation is playing
        /// </summary>
        public bool IsPlaying(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return false;
                
            return states[handle.SlotIndex].IsPlaying;
        }
        
        /// <summary>
        /// Check if animation is paused
        /// </summary>
        public bool IsPaused(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return false;
                
            return states[handle.SlotIndex].IsPaused;
        }
        
        /// <summary>
        /// Check if any animation is playing
        /// </summary>
        public bool IsAnyPlaying()
        {
            return activeStateCount > 0;
        }
        
        #endregion
        
        #region Batch Queries
        
        /// <summary>
        /// Get all active animation handles
        /// </summary>
        public AnimationHandle[] GetActiveHandles()
        {
            var handles = new AnimationHandle[activeStateCount];
            int index = 0;
            
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].IsPlaying)
                {
                    handles[index++] = new AnimationHandle(
                        i,
                        states[i].Version,
                        states[i].ClipID,
                        this
                    );
                    
                    if (index >= activeStateCount)
                        break;
                }
            }
            
            return handles;
        }
        
        /// <summary>
        /// Batch get progress for multiple handles
        /// </summary>
        public float[] BatchGetProgress(AnimationHandle[] handles)
        {
            var progress = new float[handles.Length];
            
            for (int i = 0; i < handles.Length; i++)
            {
                progress[i] = GetProgress(handles[i]);
            }
            
            return progress;
        }
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Set completion callback for animation
        /// </summary>
        public void SetOnComplete(AnimationHandle handle, System.Action callback)
        {
            if (!IsHandleValid(handle))
                return;
                
            states[handle.SlotIndex].OnComplete = callback;
        }
        
        /// <summary>
        /// Set loop callback for animation
        /// </summary>
        public void SetOnLoop(AnimationHandle handle, System.Action callback)
        {
            if (!IsHandleValid(handle))
                return;
                
            states[handle.SlotIndex].OnLoop = callback;
        }
        
        #endregion
        
        #region Debug Info
        
        /// <summary>
        /// Get debug information for handle
        /// </summary>
        public string GetDebugInfo(AnimationHandle handle)
        {
            if (!IsHandleValid(handle))
                return "Invalid Handle";
                
            var state = states[handle.SlotIndex];
            return $"Slot:{handle.SlotIndex} " +
                   $"Time:{state.CurrentTime:F2}/{state.Length:F2} " +
                   $"Weight:{state.Weight:F2} " +
                   $"Speed:{state.Speed:F2} " +
                   $"Loops:{state.LoopCount}";
        }
        
        /// <summary>
        /// Get overall system debug info
        /// </summary>
        public string GetSystemDebugInfo()
        {
            return $"Active:{activeStateCount}/{AnimationConstants.MAX_SLOTS} " +
                   $"Queue:{animationQueue.Count} " +
                   $"Graph:{(graphPaused ? "Paused" : "Active")} " +
                   $"GlobalSpeed:{globalSpeed:F2}";
        }
        
        #endregion
    }
}