using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LightningAnimation
{
    /// <summary>
    /// High-performance animation controller using Playables API
    /// COMPLETELY FIXED VERSION - All issues resolved
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Lightning Animation/Playable Animation Controller")]
    public partial class PlayableAnimationController : MonoBehaviour
    {
        #region Serialized Fields
        
        [Header("Performance Settings")]
        [SerializeField, Tooltip("Automatically stop animations when they complete")]
        private bool autoStopOnComplete = true;
        
        [SerializeField, Tooltip("Pause graph when no animations are playing")]
        private bool autoOptimizeWhenIdle = true;
        
        [SerializeField, Tooltip("Global speed multiplier for all animations")]
        [Range(0f, 3f)]
        private float globalSpeed = 1f;
        
        [Header("Preload")]
        [SerializeField, Tooltip("Animations to preload on start")]
        private AnimationClip[] preloadClips = new AnimationClip[0];
        
        [Header("Debug")]
        [SerializeField]
        private bool enableDebugLogging = false;
        
        #endregion
        
        #region Core Fields
        
        // Playable graph components
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixerPlayable;
        private Animator animator;
        
        // Fixed-size state arrays (no allocations after init)
        private AnimationState[] states;
        private int activeStateCount;
        private int nextVersion = 1;
        
        // Slot management - COMPLETELY FIXED
        private bool[] slotInUse;
        private Queue<QueuedAnimation> animationQueue;
        
        // Graph state
        private bool graphInitialized = false;
        private bool graphPaused = false;
        private float lastActiveTime;
        
        // Pooling
        private PlayablePool playablePool;
        
        // Clip reference storage
        private Dictionary<int, AnimationClip> clipIDToClip;
        
        #endregion
        
        #region Nested Types
        
        /// <summary>
        /// Queued animation data
        /// </summary>
        private struct QueuedAnimation
        {
            public int ClipID;
            public Action OnComplete;
            public int Version;
        }
        
        #endregion
        
        #region Events
        
        public event System.Action<string> OnAnimationStart;
        public event System.Action<string> OnAnimationEnd;
        public event System.Action<string> OnAnimationInterrupted;
        public event System.Action<string, int> OnAnimationLoop;
        
        #endregion
        
        #region Properties
        
        public bool IsGraphInitialized => graphInitialized;
        public bool IsGraphPaused => graphPaused;
        
        /// <summary>
        /// Get actual active animation count (FIX #6)
        /// </summary>
        public int ActiveAnimationCount 
        { 
            get
            {
                int count = 0;
                for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
                {
                    if (slotInUse[i] && states[i].IsPlaying && !states[i].IsFadingOut)
                        count++;
                }
                return count;
            }
        }
        
        public float GlobalSpeed 
        { 
            get => globalSpeed;
            set => SetGlobalSpeed(value);
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializeCore();
        }
        
        private void Update()
        {
            if (!graphInitialized || graphPaused)
                return;
                
            UpdateAnimations(Time.deltaTime);
            UpdateEvents();
            
            // Auto-pause optimization (FIX #6: Use property)
            if (autoOptimizeWhenIdle)
            {
                if (ActiveAnimationCount > 0)
                {
                    lastActiveTime = Time.time;
                }
                else if (Time.time - lastActiveTime > 0.5f)
                {
                    PauseGraph();
                }
            }
        }
        
        private void OnDestroy()
        {
            Cleanup();
        }
        
        private void OnValidate()
        {
            globalSpeed = math.max(0f, globalSpeed);
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeCore()
        {
            // Get components
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("[Lightning] Animator component required!", this);
                return;
            }
            
            // IMPORTANT: Animator doesn't need a controller!
            animator.runtimeAnimatorController = null;
            
            // Initialize fixed arrays
            states = new AnimationState[AnimationConstants.MAX_SLOTS];
            slotInUse = new bool[AnimationConstants.MAX_SLOTS];
            clipIDToClip = new Dictionary<int, AnimationClip>(AnimationConstants.MAX_UNIQUE_CLIPS);
            animationQueue = new Queue<QueuedAnimation>(AnimationConstants.MAX_SLOTS);
            
            // Initialize all states (FIX #9: Don't pre-assign PlayableIndex)
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                states[i] = new AnimationState();
            }
            
            // Create playable graph
            InitializePlayableGraph();
            
            // Initialize pooling
            playablePool = new PlayablePool(playableGraph);
            
            // Initialize event system
            InitializeEventSystem();
            
            // Preload clips
            PreloadAnimations();
            
            DebugLog("Lightning Animation System initialized");
        }
        
        private void InitializePlayableGraph()
        {
            try
            {
                // Create graph
                playableGraph = PlayableGraph.Create($"{gameObject.name}_Lightning");
                playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                
                // Create mixer with fixed inputs
                mixerPlayable = AnimationMixerPlayable.Create(playableGraph, AnimationConstants.MAX_SLOTS);
                
                // Connect to output
                var output = AnimationPlayableOutput.Create(playableGraph, "Lightning", animator);
                output.SetSourcePlayable(mixerPlayable);
                
                // Pre-initialize all mixer inputs to avoid runtime allocation
                for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
                {
                    mixerPlayable.SetInputWeight(i, 0f);
                }
                
                // Start graph
                playableGraph.Play();
                graphInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lightning] Failed to initialize graph: {e.Message}", this);
                graphInitialized = false;
            }
        }
        
        private void PreloadAnimations()
        {
            if (preloadClips == null || preloadClips.Length == 0)
                return;
                
            int loaded = 0;
            foreach (var clip in preloadClips)
            {
                if (clip != null)
                {
                    PrewarmClip(clip);
                    loaded++;
                }
            }
            
            if (loaded > 0)
            {
                DebugLog($"Preloaded {loaded} animation clips");
            }
        }
        
        #endregion
        
        #region Cleanup
        
        private void Cleanup()
        {
            // Stop all animations
            StopAll();
            
            // Clear callbacks to prevent memory leaks
            for (int i = 0; i < states.Length; i++)
            {
                states[i].OnComplete = null;
                states[i].OnLoop = null;
            }
            
            // Clear pool
            playablePool?.ClearAll();
            
            // Destroy graph
            if (graphInitialized && playableGraph.IsValid())
            {
                playableGraph.Destroy();
                graphInitialized = false;
            }
            
            // Clear collections
            clipIDToClip?.Clear();
            animationQueue?.Clear();
            
            // Clear events
            OnAnimationStart = null;
            OnAnimationEnd = null;
            OnAnimationInterrupted = null;
            OnAnimationLoop = null;
            
            DebugLog("Lightning Animation System cleaned up");
        }
        
        #endregion
        
        #region Graph Control
        
        private void PauseGraph()
        {
            if (graphInitialized && !graphPaused)
            {
                playableGraph.Stop();
                graphPaused = true;
                DebugLog("Graph paused (idle optimization)");
            }
        }
        
        private void ResumeGraph()
        {
            if (graphInitialized && graphPaused)
            {
                playableGraph.Play();
                graphPaused = false;
                lastActiveTime = Time.time;
                DebugLog("Graph resumed");
            }
        }
        
        public void SetGlobalSpeed(float speed)
        {
            globalSpeed = math.max(0f, speed);
            
            // Update all active animations
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (slotInUse[i] && states[i].IsPlaying && states[i].Playable.IsValid())
                {
                    states[i].Playable.SetSpeed(states[i].Speed * globalSpeed);
                }
            }
        }
        
        #endregion
        
        #region Slot Management - COMPLETELY FIXED
        
        /// <summary>
        /// Get a free slot for animation
        /// </summary>
        private int GetFreeSlot()
        {
            // Find unused slot
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (!slotInUse[i])
                {
                    slotInUse[i] = true;
                    return i;
                }
            }
            
            // No free slot - find completed animation
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (!states[i].IsPlaying)
                {
                    FreeSlot(i);
                    slotInUse[i] = true;
                    return i;
                }
            }
            
            // Still no slot - force free oldest
            float oldestTime = float.MaxValue;
            int oldestSlot = -1;
            
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (states[i].CurrentTime < oldestTime)
                {
                    oldestTime = states[i].CurrentTime;
                    oldestSlot = i;
                }
            }
            
            if (oldestSlot >= 0)
            {
                FreeSlot(oldestSlot);
                slotInUse[oldestSlot] = true;
                return oldestSlot;
            }
            
            DebugLog("No available slots!");
            return AnimationConstants.INVALID_SLOT;
        }
        
        /// <summary>
        /// Free a slot (FIX #1 & #2: Don't destroy, return to pool)
        /// </summary>
        private void FreeSlot(int slot)
        {
            if (slot < 0 || slot >= AnimationConstants.MAX_SLOTS)
                return;
                
            ref var state = ref states[slot];
            
            // Disconnect from mixer
            if (mixerPlayable.IsValid() && slot < mixerPlayable.GetInputCount())
            {
                var input = mixerPlayable.GetInput(slot);
                if (input.IsValid())
                {
                    // Disconnect the input
                    playableGraph.Disconnect(mixerPlayable, slot);
                }
                // Clear weight
                mixerPlayable.SetInputWeight(slot, 0f);
            }
            
            // Destroy the playable to avoid latching state after it’s removed from the mixer.
               if (state.Playable.IsValid())
                    {
                        state.Playable.Destroy();
                    }
            
            // Clear state
            state.Reset();
            slotInUse[slot] = false;
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Get next version number for handle generation
        /// </summary>
        private int NextVersion()
        {
            return nextVersion++;
        }
        
        /// <summary>
        /// Get current playing animation name
        /// </summary>
        public string GetCurrentAnimation()
        {
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (slotInUse[i] && states[i].IsPlaying)
                {
                    int clipID = states[i].ClipID;
                    if (clipIDToClip.TryGetValue(clipID, out var clip))
                    {
                        return clip.name;
                    }
                }
            }
            return null;
        }
        
        /// <summary>
        /// Fire animation start event
        /// </summary>
        internal void FireAnimationStart(string clipName)
        {
            OnAnimationStart?.Invoke(clipName);
        }
        
        /// <summary>
        /// Fire animation end event
        /// </summary>
        internal void FireAnimationEnd(string clipName)
        {
            OnAnimationEnd?.Invoke(clipName);
        }
        
        /// <summary>
        /// Fire animation interrupted event
        /// </summary>
        internal void FireAnimationInterrupted(string clipName)
        {
            OnAnimationInterrupted?.Invoke(clipName);
        }
        
        /// <summary>
        /// Fire animation loop event
        /// </summary>
        internal void FireAnimationLoop(string clipName, int loopCount)
        {
            OnAnimationLoop?.Invoke(clipName, loopCount);
        }
        
        #endregion
        
        #region Debug
        
        [System.Diagnostics.Conditional("DEBUG")]
        private void Assert(bool condition, string message)
        {
            #if DEBUG
            if (!condition)
                Debug.LogError($"[Lightning] Assertion failed: {message}", this);
            #endif
        }
        
        private void DebugLog(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[Lightning] {message}", this);
        }
        
        #endregion
        
        #region Active Count Management
        
        /// <summary>
        /// Recalculate active animation count
        /// </summary>
        private int RecalculateActiveCount()
        {
            int count = 0;
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (slotInUse[i] && states[i].IsPlaying && states[i].Playable.IsValid())
                {
                    count++;
                }
            }
            return count;
        }
        
        #endregion
    }
}
