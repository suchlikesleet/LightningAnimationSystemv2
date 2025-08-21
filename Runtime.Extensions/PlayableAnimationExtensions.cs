using System;
using UnityEngine;

namespace LightningAnimation
{
    /// <summary>
    /// Extension methods for GameObject and Component
    /// Provides convenient animation control while maintaining performance
    /// </summary>
    public static class PlayableAnimationExtensions
    {
        #region GameObject Extensions
        
        /// <summary>
        /// Play animation on GameObject (by clip reference)
        /// </summary>
        public static AnimationHandle PlayAnimation(this GameObject gameObject, AnimationClip clip, Action onComplete = null)
        {
            var controller = GetOrAddController(gameObject);
            return controller != null ? controller.Play(clip, onComplete) : AnimationHandle.Invalid;
        }
        
        /// <summary>
        /// Play animation with crossfade
        /// </summary>
        public static AnimationHandle PlayAnimationWithCrossfade(this GameObject gameObject, AnimationClip clip, 
            float fadeTime = 0.3f, Action onComplete = null)
        {
            var controller = GetOrAddController(gameObject);
            return controller != null ? controller.PlayWithCrossfade(clip, fadeTime, onComplete) : AnimationHandle.Invalid;
        }
        
        /// <summary>
        /// Play animation with specific mode
        /// </summary>
        public static AnimationHandle PlayAnimationWithMode(this GameObject gameObject, AnimationClip clip, 
            PlayMode mode, Action onComplete = null)
        {
            var controller = GetOrAddController(gameObject);
            return controller != null ? controller.PlayWithMode(clip, mode, onComplete) : AnimationHandle.Invalid;
        }
        
        /// <summary>
        /// Play looped animation
        /// </summary>
        public static AnimationHandle PlayAnimationLooped(this GameObject gameObject, AnimationClip clip, 
            int loopCount = -1, Action onComplete = null)
        {
            var controller = GetOrAddController(gameObject);
            return controller != null ? controller.PlayLooped(clip, loopCount, onComplete) : AnimationHandle.Invalid;
        }
        
        /// <summary>
        /// Stop all animations on GameObject
        /// </summary>
        public static void StopAllAnimations(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            controller?.StopAll();
        }
        
        /// <summary>
        /// Stop specific animation by handle
        /// </summary>
        public static void StopAnimation(this GameObject gameObject, AnimationHandle handle)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            controller?.Stop(handle);
        }
        
        /// <summary>
        /// Pause all animations
        /// </summary>
        public static void PauseAllAnimations(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            controller?.PauseAll();
        }
        
        /// <summary>
        /// Resume all animations
        /// </summary>
        public static void ResumeAllAnimations(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            controller?.ResumeAll();
        }
        
        /// <summary>
        /// Check if any animation is playing
        /// </summary>
        public static bool IsPlayingAnimation(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            return controller != null && controller.IsAnyPlaying();
        }
        
        /// <summary>
        /// Check if specific animation is playing
        /// </summary>
        public static bool IsPlayingAnimation(this GameObject gameObject, AnimationHandle handle)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            return controller != null && controller.IsPlaying(handle);
        }
        
        /// <summary>
        /// Get all active animation handles
        /// </summary>
        public static AnimationHandle[] GetActiveAnimations(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            return controller != null ? controller.GetActiveHandles() : new AnimationHandle[0];
        }
        
        #endregion
        
        #region Component Extensions
        
        /// <summary>
        /// Play animation on Component's GameObject
        /// </summary>
        public static AnimationHandle PlayAnimation(this Component component, AnimationClip clip, Action onComplete = null)
        {
            return component.gameObject.PlayAnimation(clip, onComplete);
        }
        
        /// <summary>
        /// Play animation with crossfade
        /// </summary>
        public static AnimationHandle PlayAnimationWithCrossfade(this Component component, AnimationClip clip, 
            float fadeTime = 0.3f, Action onComplete = null)
        {
            return component.gameObject.PlayAnimationWithCrossfade(clip, fadeTime, onComplete);
        }
        
        /// <summary>
        /// Stop all animations
        /// </summary>
        public static void StopAllAnimations(this Component component)
        {
            component.gameObject.StopAllAnimations();
        }
        
        /// <summary>
        /// Check if any animation is playing
        /// </summary>
        public static bool IsPlayingAnimation(this Component component)
        {
            return component.gameObject.IsPlayingAnimation();
        }
        
        #endregion
        
        #region Utility Extensions
        
        /// <summary>
        /// Get or add PlayableAnimationController to GameObject
        /// </summary>
        public static PlayableAnimationController GetOrAddAnimationController(this GameObject gameObject)
        {
            return GetOrAddController(gameObject);
        }
        
        /// <summary>
        /// Get PlayableAnimationController (returns null if not found)
        /// </summary>
        public static PlayableAnimationController GetAnimationController(this GameObject gameObject)
        {
            return gameObject.GetComponent<PlayableAnimationController>();
        }
        
        /// <summary>
        /// Check if GameObject has PlayableAnimationController
        /// </summary>
        public static bool HasAnimationController(this GameObject gameObject)
        {
            return gameObject.GetComponent<PlayableAnimationController>() != null;
        }
        
        /// <summary>
        /// Setup animation controller with clips
        /// </summary>
        public static PlayableAnimationController SetupAnimationController(this GameObject gameObject, 
            params AnimationClip[] clips)
        {
            var controller = GetOrAddController(gameObject);
            
            if (controller != null && clips != null)
            {
                foreach (var clip in clips)
                {
                    if (clip != null)
                    {
                        controller.PrewarmClip(clip);
                    }
                }
            }
            
            return controller;
        }
        
        /// <summary>
        /// Set global speed for all animations
        /// </summary>
        public static void SetAnimationSpeed(this GameObject gameObject, float speed)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            controller?.SetGlobalSpeed(speed);
        }
        
        #endregion
        
        #region Batch Operations
        
        /// <summary>
        /// Play multiple animations in sequence
        /// </summary>
        public static AnimationHandle[] PlayAnimationSequence(this GameObject gameObject, 
            AnimationClip[] clips, Action onSequenceComplete = null)
        {
            var controller = GetOrAddController(gameObject);
            if (controller == null || clips == null || clips.Length == 0)
                return new AnimationHandle[0];
            
            var handles = new AnimationHandle[clips.Length];
            
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    Action callback = (i == clips.Length - 1) ? onSequenceComplete : null;
                    handles[i] = controller.PlayWithMode(clips[i], PlayMode.Queue, callback);
                }
            }
            
            return handles;
        }
        
        /// <summary>
        /// Batch set speed for all active animations
        /// </summary>
        public static void SetAllAnimationSpeeds(this GameObject gameObject, float speed)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            if (controller != null)
            {
                var handles = controller.GetActiveHandles();
                controller.BatchSetSpeed(handles, speed);
            }
        }
        
        /// <summary>
        /// Get progress of all active animations
        /// </summary>
        public static float[] GetAllAnimationProgress(this GameObject gameObject)
        {
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            if (controller != null)
            {
                var handles = controller.GetActiveHandles();
                return controller.BatchGetProgress(handles);
            }
            return new float[0];
        }
        
        #endregion
        
        #region Helper Methods
        
        private static PlayableAnimationController GetOrAddController(GameObject gameObject)
        {
            if (gameObject == null)
                return null;
            
            var controller = gameObject.GetComponent<PlayableAnimationController>();
            if (controller == null)
            {
                // Ensure Animator exists
                if (gameObject.GetComponent<Animator>() == null)
                {
                    gameObject.AddComponent<Animator>();
                }
                
                controller = gameObject.AddComponent<PlayableAnimationController>();
            }
            
            return controller;
        }
        
        #endregion
        
        #region Fluent API Helpers
        
        /// <summary>
        /// Fluent API for chaining operations
        /// </summary>
        public static AnimationBuilder Animate(this GameObject gameObject)
        {
            return new AnimationBuilder(GetOrAddController(gameObject));
        }
        
        /// <summary>
        /// Fluent API for chaining operations on components
        /// </summary>
        public static AnimationBuilder Animate(this Component component)
        {
            return new AnimationBuilder(GetOrAddController(component.gameObject));
        }
        
        #endregion
    }
    
    /// <summary>
    /// Fluent API builder for animation operations
    /// </summary>
    public class AnimationBuilder
    {
        private readonly PlayableAnimationController controller;
        private AnimationHandle currentHandle;
        
        internal AnimationBuilder(PlayableAnimationController controller)
        {
            this.controller = controller;
            currentHandle = AnimationHandle.Invalid;
        }
        
        public AnimationBuilder Play(AnimationClip clip)
        {
            if (controller != null && clip != null)
            {
                currentHandle = controller.Play(clip);
            }
            return this;
        }
        
        public AnimationBuilder WithCrossfade(AnimationClip clip, float fadeTime = 0.3f)
        {
            if (controller != null && clip != null)
            {
                currentHandle = controller.PlayWithCrossfade(clip, fadeTime);
            }
            return this;
        }
        
        public AnimationBuilder Queue(AnimationClip clip)
        {
            if (controller != null && clip != null)
            {
                currentHandle = controller.PlayWithMode(clip, PlayMode.Queue);
            }
            return this;
        }
        
        public AnimationBuilder SetSpeed(float speed)
        {
            if (controller != null && currentHandle.IsValid)
            {
                controller.SetSpeed(currentHandle, speed);
            }
            return this;
        }
        
        public AnimationBuilder SetWeight(float weight)
        {
            if (controller != null && currentHandle.IsValid)
            {
                controller.SetWeight(currentHandle, weight);
            }
            return this;
        }
        
        public AnimationBuilder SetLooping(bool loop, int maxLoops = -1)
        {
            if (controller != null && currentHandle.IsValid)
            {
                controller.SetLooping(currentHandle, loop, maxLoops);
            }
            return this;
        }
        
        public AnimationBuilder OnComplete(Action callback)
        {
            if (controller != null && currentHandle.IsValid)
            {
                controller.SetOnComplete(currentHandle, callback);
            }
            return this;
        }
        
        public AnimationBuilder OnLoop(Action callback)
        {
            if (controller != null && currentHandle.IsValid)
            {
                controller.SetOnLoop(currentHandle, callback);
            }
            return this;
        }
        
        public AnimationHandle GetHandle()
        {
            return currentHandle;
        }
    }
}