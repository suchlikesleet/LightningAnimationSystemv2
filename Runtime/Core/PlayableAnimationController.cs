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
    /// Core functionality and initialization
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
        
        // Fast lookups using clip instance IDs
        private Dictionary<int, int> clipIDToSlot;
        private Dictionary<int, AnimationClipPlayable> clipIDToPlayable;
        
        // Slot management
        private byte slotOccupancy = 0; // Bit mask for 8 slots
        private Queue<AnimationHandle> animationQueue;
        
        // Graph state
        private bool graphInitialized = false;
        private bool graphPaused = false;
        private float lastActiveTime;
        
        // Pooling
        private PlayablePool playablePool;
        
        // Clip reference storage
        private Dictionary<int, AnimationClip> clipIDToClip;
        
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
        public int ActiveAnimationCount => activeStateCount;
        public float GlobalSpeed => globalSpeed;
        
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
            
            // Auto-pause optimization
            if (autoOptimizeWhenIdle)
            {
                if (activeStateCount > 0)
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
            
            // Initialize fixed arrays
            states = new AnimationState[AnimationConstants.MAX_SLOTS];
            clipIDToSlot = new Dictionary<int, int>(AnimationConstants.MAX_SLOTS);
            clipIDToPlayable = new Dictionary<int, AnimationClipPlayable>(AnimationConstants.MAX_UNIQUE_CLIPS);
            clipIDToClip = new Dictionary<int, AnimationClip>(AnimationConstants.MAX_UNIQUE_CLIPS);
            animationQueue = new Queue<AnimationHandle>(AnimationConstants.MAX_SLOTS);
            
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
            
            // Destroy playables
            foreach (var playable in clipIDToPlayable.Values)
            {
                if (playable.IsValid())
                    playable.Destroy();
            }
            
            // Destroy graph
            if (graphInitialized && playableGraph.IsValid())
            {
                playableGraph.Destroy();
                graphInitialized = false;
            }
            
            // Clear collections
            clipIDToSlot?.Clear();
            clipIDToPlayable?.Clear();
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
            
            if (graphInitialized)
            {
                playableGraph.GetRootPlayable(0).SetSpeed(globalSpeed);
            }
        }
        
        #endregion
        
        #region Slot Management
        
        /// <summary>
        /// Get or assign a slot for the given clip
        /// </summary>
        private int GetOrAssignSlot(int clipID)
        {
            // Check if already assigned
            if (clipIDToSlot.TryGetValue(clipID, out int existingSlot))
            {
                if (existingSlot >= 0 && existingSlot < AnimationConstants.MAX_SLOTS)
                    return existingSlot;
            }
            
            // Find free slot using bit operations (super fast)
            int freeSlot = GetFreeSlot();
            if (freeSlot >= 0)
            {
                clipIDToSlot[clipID] = freeSlot;
                SetSlotOccupied(freeSlot, true);
                return freeSlot;
            }
            
            // No free slots - try to free the oldest
            for (int i = 0; i < AnimationConstants.MAX_SLOTS; i++)
            {
                if (!states[i].IsPlaying)
                {
                    FreeSlot(i);
                    clipIDToSlot[clipID] = i;
                    SetSlotOccupied(i, true);
                    return i;
                }
            }
            
            DebugLog("No available slots!");
            return AnimationConstants.INVALID_SLOT;
        }
        
        /// <summary>
        /// Find free slot using bit manipulation (branchless)
        /// </summary>
        private int GetFreeSlot()
        {
            byte freeMask = (byte)(~slotOccupancy & 0xFF);
            if (freeMask == 0) return AnimationConstants.INVALID_SLOT;
            
            // Count trailing zeros to find first free slot
            int slot = 0;
            while ((freeMask & 1) == 0 && slot < AnimationConstants.MAX_SLOTS)
            {
                freeMask >>= 1;
                slot++;
            }
            
            return slot < AnimationConstants.MAX_SLOTS ? slot : AnimationConstants.INVALID_SLOT;
        }
        
        private void SetSlotOccupied(int slot, bool occupied)
        {
            if (occupied)
                slotOccupancy |= (byte)(1 << slot);
            else
                slotOccupancy &= (byte)~(1 << slot);
        }
        
        private void FreeSlot(int slot)
        {
            if (slot < 0 || slot >= AnimationConstants.MAX_SLOTS)
                return;
                
            ref var state = ref states[slot];
            
            // Disconnect from mixer
            if (state.PlayableIndex >= 0)
            {
                mixerPlayable.SetInputWeight(state.PlayableIndex, 0f);
            }
            
            // Clear state
            state.Reset();
            SetSlotOccupied(slot, false);
            
            // Remove from clip mapping
            if (state.ClipID != 0)
            {
                clipIDToSlot.Remove(state.ClipID);
            }
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
                if (states[i].IsPlaying)
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
    }
}