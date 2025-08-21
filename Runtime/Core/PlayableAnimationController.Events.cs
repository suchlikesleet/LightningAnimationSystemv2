using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace LightningAnimation
{
    /// <summary>
    /// Event system for PlayableAnimationController
    /// Handles curve-based events with zero allocations
    /// </summary>
    public partial class PlayableAnimationController
    {
        #region Event Data Structures
        
        /// <summary>
        /// Event trigger data
        /// </summary>
        private struct EventTrigger
        {
            public int ClipID;
            public float TriggerTime;
            public Action Callback;
            public bool HasFired;
            public int SlotIndex;
        }
        
        /// <summary>
        /// Curve-based event detector
        /// </summary>
        private struct CurveEvent
        {
            public string CurveName;
            public float Threshold;
            public float PreviousValue;
            public Action<float> OnTrigger;
        }
        
        #endregion
        
        #region Event Fields
        
        // Fixed-size event buffers (no allocations)
        private EventTrigger[] eventTriggers;
        private int eventTriggerCount;
        
        private CurveEvent[] curveEvents;
        private int curveEventCount;
        
        // Event curve sampling
        private const string EVENT_CURVE_PREFIX = "LAS.Event.";
        private const float EVENT_THRESHOLD = 0.5f;
        
        #endregion
        
        #region Event Initialization
        
        private void InitializeEventSystem()
        {
            eventTriggers = new EventTrigger[AnimationConstants.EVENT_BUFFER_SIZE];
            curveEvents = new CurveEvent[AnimationConstants.EVENT_BUFFER_SIZE];
            eventTriggerCount = 0;
            curveEventCount = 0;
        }
        
        #endregion
        
        #region Time-based Events
        
        /// <summary>
        /// Register a time-based event for an animation
        /// </summary>
        public void RegisterTimeEvent(AnimationClip clip, float normalizedTime, Action callback)
        {
            if (clip == null || callback == null || eventTriggerCount >= AnimationConstants.EVENT_BUFFER_SIZE)
                return;
            
            int clipID = clip.GetInstanceID();
            float triggerTime = normalizedTime * clip.length;
            
            // Find or add event
            for (int i = 0; i < eventTriggerCount; i++)
            {
                if (eventTriggers[i].ClipID == clipID && 
                    math.abs(eventTriggers[i].TriggerTime - triggerTime) < 0.01f)
                {
                    // Update existing
                    eventTriggers[i].Callback = callback;
                    return;
                }
            }
            
            // Add new event
            eventTriggers[eventTriggerCount] = new EventTrigger
            {
                ClipID = clipID,
                TriggerTime = triggerTime,
                Callback = callback,
                HasFired = false,
                SlotIndex = -1
            };
            eventTriggerCount++;
        }
        
        /// <summary>
        /// Clear events for a specific clip
        /// </summary>
        public void ClearTimeEvents(AnimationClip clip)
        {
            if (clip == null) return;
            
            int clipID = clip.GetInstanceID();
            
            // Compact array, removing matching events
            int writeIndex = 0;
            for (int i = 0; i < eventTriggerCount; i++)
            {
                if (eventTriggers[i].ClipID != clipID)
                {
                    if (writeIndex != i)
                        eventTriggers[writeIndex] = eventTriggers[i];
                    writeIndex++;
                }
            }
            eventTriggerCount = writeIndex;
        }
        
        /// <summary>
        /// Process time-based events
        /// </summary>
        private void ProcessTimeEvents()
        {
            for (int i = 0; i < eventTriggerCount; i++)
            {
                ref var evt = ref eventTriggers[i];
                
                // Find matching animation
                for (int slot = 0; slot < AnimationConstants.MAX_SLOTS; slot++)
                {
                    ref var state = ref states[slot];
                    if (!state.IsPlaying || state.ClipID != evt.ClipID)
                        continue;
                    
                    // Check if we crossed the trigger time
                    bool shouldTrigger = false;
                    
                    if (evt.HasFired)
                    {
                        // Reset if animation restarted
                        if (state.CurrentTime < evt.TriggerTime * 0.5f)
                        {
                            evt.HasFired = false;
                        }
                    }
                    else
                    {
                        // Check if we passed the trigger time
                        if (state.CurrentTime >= evt.TriggerTime)
                        {
                            shouldTrigger = true;
                            evt.HasFired = true;
                        }
                    }
                    
                    if (shouldTrigger)
                    {
                        evt.Callback?.Invoke();
                    }
                }
            }
        }
        
        #endregion
        
        #region Curve-based Events
        
        /// <summary>
        /// Register a curve-based event
        /// </summary>
        public void RegisterCurveEvent(string curveName, float threshold, Action<float> onTrigger)
        {
            if (string.IsNullOrEmpty(curveName) || onTrigger == null || 
                curveEventCount >= AnimationConstants.EVENT_BUFFER_SIZE)
                return;
            
            // Check if already exists
            for (int i = 0; i < curveEventCount; i++)
            {
                if (curveEvents[i].CurveName == curveName)
                {
                    curveEvents[i].Threshold = threshold;
                    curveEvents[i].OnTrigger = onTrigger;
                    return;
                }
            }
            
            // Add new
            curveEvents[curveEventCount] = new CurveEvent
            {
                CurveName = EVENT_CURVE_PREFIX + curveName,
                Threshold = threshold,
                PreviousValue = 0f,
                OnTrigger = onTrigger
            };
            curveEventCount++;
        }
        
        /// <summary>
        /// Sample animation curves for events
        /// </summary>
        private void ProcessCurveEvents()
        {
            if (curveEventCount == 0) return;
            
            for (int slot = 0; slot < AnimationConstants.MAX_SLOTS; slot++)
            {
                ref var state = ref states[slot];
                if (!state.IsPlaying || !state.HasFlag(AnimationFlags.HasEvents))
                    continue;
                
                // Get clip
                if (!clipIDToClip.TryGetValue(state.ClipID, out var clip))
                    continue;
                
                // Sample curves (this is where you'd implement actual curve sampling)
                // For now, this is a placeholder for the curve sampling logic
                ProcessClipCurves(clip, state.NormalizedTime);
            }
        }
        
        /// <summary>
        /// Process curves for a specific clip
        /// </summary>
        private void ProcessClipCurves(AnimationClip clip, float normalizedTime)
        {
            // This would sample the actual animation curves
            // Unity's AnimationClip.SampleAnimation or custom curve evaluation
            // Placeholder for actual implementation
            
            for (int i = 0; i < curveEventCount; i++)
            {
                ref var evt = ref curveEvents[i];
                
                // Simulate curve sampling (replace with actual curve evaluation)
                float currentValue = SampleCurve(clip, evt.CurveName, normalizedTime);
                
                // Detect edge crossing
                if (evt.PreviousValue < evt.Threshold && currentValue >= evt.Threshold)
                {
                    evt.OnTrigger?.Invoke(currentValue);
                }
                
                evt.PreviousValue = currentValue;
            }
        }
        
        /// <summary>
        /// Sample a curve value (placeholder - implement actual curve sampling)
        /// </summary>
        private float SampleCurve(AnimationClip clip, string curveName, float normalizedTime)
        {
            // This is where you'd implement actual curve sampling
            // For now, return a simple sine wave for demonstration
            return math.sin(normalizedTime * math.PI * 2f);
        }
        
        #endregion
        
        #region Event Update
        
        /// <summary>
        /// Update all events (called from main update loop)
        /// </summary>
        private void UpdateEvents()
        {
            ProcessTimeEvents();
            ProcessCurveEvents();
        }
        
        #endregion
        
        #region Event Utilities
        
        /// <summary>
        /// Clear all events
        /// </summary>
        public void ClearAllEvents()
        {
            eventTriggerCount = 0;
            curveEventCount = 0;
            
            // Clear event flags from states
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                states[i].SetFlag(AnimationFlags.HasEvents, false);
            }
        }
        
        /// <summary>
        /// Mark animation as having events
        /// </summary>
        public void MarkHasEvents(AnimationClip clip)
        {
            if (clip == null) return;
            
            int clipID = clip.GetInstanceID();
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].ClipID == clipID)
                {
                    states[i].SetFlag(AnimationFlags.HasEvents, true);
                }
            }
        }
        
        #endregion
    }
}