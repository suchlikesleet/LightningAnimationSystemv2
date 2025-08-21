namespace LightningAnimation
{
    /// <summary>
    /// Core constants and flags for the Lightning Animation System
    /// </summary>
    public static class AnimationConstants
    {
        // System limits
        public const int MAX_SLOTS = 8;
        public const int MAX_CONCURRENT_ANIMATIONS = 8;
        public const int POOL_SIZE_PER_CLIP = 4;
        public const int MAX_UNIQUE_CLIPS = 32;
        public const int EVENT_BUFFER_SIZE = 16;
        
        // Performance tuning
        public const float MIN_WEIGHT_THRESHOLD = 0.001f;
        public const float DEFAULT_BLEND_SPEED = 5f;
        public const float DEFAULT_FADE_TIME = 0.3f;
        
        // Version control
        public const int INVALID_VERSION = -1;
        public const int INVALID_SLOT = -1;
        
        // Update modes
        public const int BATCH_SIZE = 4; // Process 4 animations at once with SIMD
    }
    
    /// <summary>
    /// Bit flags for animation state (32 bits available)
    /// </summary>
    [System.Flags]
    public enum AnimationFlags
    {
        None = 0,
        Playing = 1 << 0,
        Paused = 1 << 1,
        Looping = 1 << 2,
        FadingIn = 1 << 3,
        FadingOut = 1 << 4,
        Queued = 1 << 5,
        HasEvents = 1 << 6,
        AutoStop = 1 << 7,
        Initialized = 1 << 8,
        Dirty = 1 << 9,
        WeightDirty = 1 << 10,
        TimeDirty = 1 << 11,
        SpeedDirty = 1 << 12,
        // Reserve rest for future use
    }
    
    /// <summary>
    /// Play modes for animation playback
    /// </summary>
    public enum PlayMode
    {
        Single,      // Stop all other animations
        Additive,    // Play alongside other animations
        Queue        // Queue after current animation
    }
    
    /// <summary>
    /// Event types for curve-based events
    /// </summary>
    public enum EventType
    {
        Trigger,     // One-shot event
        Start,       // Animation started
        End,         // Animation ended
        Loop,        // Animation looped
        Custom       // User-defined event
    }
}