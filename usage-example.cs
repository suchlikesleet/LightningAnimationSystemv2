using UnityEngine;
using LightningAnimation;
using PlayMode = LightningAnimation.PlayMode;

/// <summary>
/// Example usage of the Lightning Animation System
/// Shows best practices and common patterns
/// </summary>
public class LightningAnimationExample : MonoBehaviour
{
    [Header("Animation Clips")]
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip jumpClip;
    [SerializeField] private AnimationClip attackClip;
    
    private PlayableAnimationController animController;
    private AnimationHandle currentHandle;
    
    private void Start()
    {
        // Get or add the controller
        animController = gameObject.GetOrAddAnimationController();
        
        // Prewarm frequently used clips for better performance
        if (idleClip) animController.PrewarmClip(idleClip);
        if (walkClip) animController.PrewarmClip(walkClip);
        if (runClip) animController.PrewarmClip(runClip);
        
        // Start with idle animation
        PlayIdle();
        
        // Subscribe to events
        animController.OnAnimationStart += OnAnimStart;
        animController.OnAnimationEnd += OnAnimEnd;
        animController.OnAnimationLoop += OnAnimLoop;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (animController != null)
        {
            animController.OnAnimationStart -= OnAnimStart;
            animController.OnAnimationEnd -= OnAnimEnd;
            animController.OnAnimationLoop -= OnAnimLoop;
        }
    }
    
    /// <summary>
    /// Example 1: Simple play with completion callback
    /// </summary>
    public void PlayAttack()
    {
        currentHandle = animController.Play(attackClip, () => {
            Debug.Log("Attack finished!");
            PlayIdle(); // Return to idle after attack
        });
    }
    
    /// <summary>
    /// Example 2: Looped animation
    /// </summary>
    public void PlayIdle()
    {
        // Loop forever (-1 means infinite loops)
        currentHandle = animController.PlayLooped(idleClip, -1);
    }
    
    /// <summary>
    /// Example 3: Crossfade between animations
    /// </summary>
    public void PlayWalk()
    {
        currentHandle = animController.PlayWithCrossfade(walkClip, 0.3f);
        
        // Optional: Set custom speed
        currentHandle.SetSpeed(1.2f);
    }
    
    /// <summary>
    /// Example 4: Queue animations
    /// </summary>
    public void PlayCombo()
    {
        // Play attack sequence
        animController.PlayWithMode(attackClip, PlayMode.Single);
        animController.PlayWithMode(attackClip, PlayMode.Queue);
        animController.PlayWithMode(jumpClip, PlayMode.Queue, () => {
            Debug.Log("Combo complete!");
            PlayIdle();
        });
    }
    
    /// <summary>
    /// Example 5: Dynamic speed control
    /// </summary>
    public void SetMovementSpeed(float speed)
    {
        if (currentHandle.IsValid && currentHandle.IsPlaying)
        {
            // Adjust animation speed based on movement
            currentHandle.Speed = Mathf.Clamp(speed, 0.5f, 2f);
        }
    }
    
    /// <summary>
    /// Example 6: Pause/Resume
    /// </summary>
    public void TogglePause()
    {
        if (currentHandle.IsValid)
        {
            if (currentHandle.IsPaused)
                currentHandle.Resume();
            else
                currentHandle.Pause();
        }
    }
    
    /// <summary>
    /// Example 7: Using the fluent API
    /// </summary>
    public void PlayWithFluentAPI()
    {
        gameObject.Animate()
            .Play(jumpClip)
            .SetSpeed(1.5f)
            .SetLooping(false)
            .OnComplete(() => Debug.Log("Jump done!"));
    }
    
    /// <summary>
    /// Example 8: Check animation state
    /// </summary>
    private void Update()
    {
        // Check if specific animation is playing
        if (currentHandle.IsValid && currentHandle.IsPlaying)
        {
            // Get normalized progress (0-1)
            float progress = currentHandle.Progress;
            
            // Do something at 50% progress
            if (progress >= 0.5f && progress < 0.51f)
            {
                // Trigger effect, sound, etc.
            }
        }
        
        // Global speed control example
        if (Input.GetKey(KeyCode.LeftShift))
        {
            animController.GlobalSpeed = 2f; // Double speed
        }
        else
        {
            animController.GlobalSpeed = 1f; // Normal speed
        }
    }
    
    /// <summary>
    /// Event callbacks
    /// </summary>
    private void OnAnimStart(string clipName)
    {
        Debug.Log($"Started: {clipName}");
    }
    
    private void OnAnimEnd(string clipName)
    {
        Debug.Log($"Ended: {clipName}");
    }
    
    private void OnAnimLoop(string clipName, int loopCount)
    {
        Debug.Log($"Loop #{loopCount}: {clipName}");
    }
    
    /// <summary>
    /// Example 9: Batch operations
    /// </summary>
    public void PlaySequence()
    {
        var clips = new AnimationClip[] { jumpClip, attackClip, idleClip };
        var handles = gameObject.PlayAnimationSequence(clips, () => {
            Debug.Log("Sequence complete!");
        });
        
        // You can still control individual animations in the sequence
        if (handles.Length > 0)
        {
            handles[0].SetSpeed(1.5f); // Speed up the jump
        }
    }
    
    /// <summary>
    /// Example 10: Advanced blending
    /// </summary>
    public void BlendAnimations()
    {
        // Play multiple animations with different weights
        var handle1 = animController.PlayWithMode(idleClip, PlayMode.Additive);
        var handle2 = animController.PlayWithMode(walkClip, PlayMode.Additive);
        
        // Set weights for blending
        handle1.Weight = 0.3f;
        handle2.Weight = 0.7f;
    }
}

/// <summary>
/// Performance tips for using Lightning Animation System
/// </summary>
public static class LightningPerformanceTips
{
    /*
    1. PREWARM CLIPS
       - Always prewarm frequently used clips at startup
       - This creates pooled instances for instant playback
    
    2. REUSE HANDLES
       - Store AnimationHandle references for animations you need to control
       - Handles are lightweight structs (12 bytes)
    
    3. USE PLAY MODES WISELY
       - Single: Stops all other animations (default)
       - Additive: Plays alongside others (for blending)
       - Queue: Waits for current to finish (for sequences)
    
    4. BATCH OPERATIONS
       - Use batch methods when controlling multiple animations
       - BatchSetSpeed, BatchGetProgress, etc.
    
    5. EVENT OPTIMIZATION
       - Only subscribe to events you actually need
       - Unsubscribe in OnDestroy to prevent leaks
    
    6. GLOBAL SPEED
       - Use GlobalSpeed for time-scale effects
       - More efficient than setting individual speeds
    
    7. AUTO-OPTIMIZATION
       - Enable autoOptimizeWhenIdle for better battery life
       - Graph auto-pauses when no animations play
    
    8. MEMORY MANAGEMENT
       - System uses fixed arrays (no runtime allocations)
       - Max 8 concurrent animations by default
       - Max 32 unique clips cached
    
    9. NO ANIMATOR CONTROLLER NEEDED
       - System works without AnimatorController asset
       - Just needs Animator component
       - Much lighter than traditional Mecanim
    
    10. SIMD OPTIMIZATION
        - System uses Unity.Mathematics for SIMD operations
        - Processes 4 animations at once where possible
    */
}
