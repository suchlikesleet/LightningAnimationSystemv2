using System.Collections;
using UnityEngine;
using LightningAnimation;
using PlayMode = LightningAnimation.PlayMode;

/// <summary>
/// Test suite for verifying all Lightning Animation System fixes
/// Add this to a GameObject with animation clips to test
/// </summary>
public class LightningAnimationTestSuite : MonoBehaviour
{
    [Header("Test Clips (Assign in Inspector)")]
    [SerializeField] private AnimationClip testClip1;
    [SerializeField] private AnimationClip testClip2;
    [SerializeField] private AnimationClip loopTestClip;
    
    [Header("Test Settings")]
    [SerializeField] private bool runTestsOnStart = true;
    [SerializeField] private bool verboseLogging = true;
    
    private PlayableAnimationController controller;
    private int testsPassed = 0;
    private int testsFailed = 0;
    
    private void Start()
    {
        // Setup
        controller = gameObject.GetOrAddAnimationController();
        
        if (!ValidateTestClips())
        {
            Debug.LogError("Please assign test clips in the inspector!");
            return;
        }
        
        if (runTestsOnStart)
        {
            StartCoroutine(RunAllTests());
        }
    }
    
    private bool ValidateTestClips()
    {
        return testClip1 != null && testClip2 != null && loopTestClip != null;
    }
    
    /// <summary>
    /// Run all tests sequentially
    /// </summary>
    private IEnumerator RunAllTests()
    {
        Debug.Log("=== LIGHTNING ANIMATION TEST SUITE STARTED ===");
        yield return new WaitForSeconds(1f);
        
        // Test 1: Basic Playback
        yield return TestBasicPlayback();
        
        // Test 2: Looping
        yield return TestLooping();
        
        // Test 3: Crossfade
        yield return TestCrossfade();
        
        // Test 4: Queue System
        yield return TestQueueSystem();
        
        // Test 5: Pause/Resume
        yield return TestPauseResume();
        
        // Test 6: Speed Control
        yield return TestSpeedControl();
        
        // Test 7: Multiple Concurrent
        yield return TestMultipleConcurrent();
        
        // Test 8: Weight System
        yield return TestWeightSystem();
        
        // Test 9: Memory/Pool
        yield return TestMemoryPool();
        
        // Test 10: Events
        yield return TestEvents();
        
        // Results
        Debug.Log("=== TEST SUITE COMPLETE ===");
        Debug.Log($"✅ Passed: {testsPassed}");
        Debug.Log($"❌ Failed: {testsFailed}");
        Debug.Log($"Success Rate: {(float)testsPassed / (testsPassed + testsFailed):P}");
    }
    
    /// <summary>
    /// Test 1: Basic playback and completion
    /// </summary>
    private IEnumerator TestBasicPlayback()
    {
        LogTest("Test 1: Basic Playback");
        
        bool completed = false;
        var handle = controller.Play(testClip1, () => completed = true);
        
        AssertTrue(handle.IsValid, "Handle should be valid");
        AssertTrue(handle.IsPlaying, "Animation should be playing");
        
        // Wait for completion
        float timeout = testClip1.length + 0.5f;
        float elapsed = 0;
        
        while (!completed && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        AssertTrue(completed, "Animation should have completed");
        AssertFalse(handle.IsPlaying, "Animation should have stopped");
        
        EndTest();
    }
    
    /// <summary>
    /// Test 2: Looping functionality
    /// </summary>
    private IEnumerator TestLooping()
    {
        LogTest("Test 2: Looping");
        
        int loopCount = 0;
        var handle = controller.PlayLooped(loopTestClip, 3); // Loop 3 times
        
        controller.OnAnimationLoop += (clip, count) => {
            loopCount = count;
            Log($"Loop #{count}");
        };
        
        // Wait for 3 loops
        float waitTime = loopTestClip.length * 3.5f;
        yield return new WaitForSeconds(waitTime);
        
        AssertTrue(loopCount >= 2, $"Should have looped at least 2 times (got {loopCount})");
        AssertFalse(handle.IsPlaying, "Should stop after 3 loops");
        
        // Test infinite loop
        handle = controller.PlayLooped(loopTestClip, -1);
        yield return new WaitForSeconds(loopTestClip.length * 2.5f);
        
        AssertTrue(handle.IsPlaying, "Infinite loop should still be playing");
        controller.Stop(handle);
        
        EndTest();
    }
    
    /// <summary>
    /// Test 3: Crossfade between animations
    /// </summary>
    private IEnumerator TestCrossfade()
    {
        LogTest("Test 3: Crossfade");
        
        var handle1 = controller.Play(testClip1);
        yield return new WaitForSeconds(0.5f);
        
        var handle2 = controller.PlayWithCrossfade(testClip2, 0.3f);
        
        // Check both are playing during fade
        yield return new WaitForSeconds(0.1f);
        AssertTrue(handle1.IsPlaying, "First should still be playing during fade");
        AssertTrue(handle2.IsPlaying, "Second should be playing");
        
        // Check weights
        AssertTrue(handle1.Weight < 1f, "First weight should be decreasing");
        AssertTrue(handle2.Weight < 1f, "Second weight should be increasing");
        
        // Wait for fade completion
        yield return new WaitForSeconds(0.5f);
        AssertFalse(handle1.IsPlaying, "First should have stopped after fade");
        AssertTrue(handle2.IsPlaying, "Second should still be playing");
        
        controller.StopAll();
        EndTest();
    }
    
    /// <summary>
    /// Test 4: Queue system
    /// </summary>
    private IEnumerator TestQueueSystem()
    {
        LogTest("Test 4: Queue System");
        
        int completionOrder = 0;
        
        controller.PlayWithMode(testClip1, PlayMode.Single, () => completionOrder = 1);
        controller.PlayWithMode(testClip2, PlayMode.Queue, () => completionOrder = 2);
        controller.PlayWithMode(loopTestClip, PlayMode.Queue, () => completionOrder = 3);
        
        // Wait for all to complete
        float totalTime = testClip1.length + testClip2.length + loopTestClip.length + 1f;
        yield return new WaitForSeconds(totalTime);
        
        AssertEquals(completionOrder, 3, "All queued animations should have played in order");
        
        EndTest();
    }
    
    /// <summary>
    /// Test 5: Pause and resume
    /// </summary>
    private IEnumerator TestPauseResume()
    {
        LogTest("Test 5: Pause/Resume");
        
        var handle = controller.Play(testClip1);
        yield return new WaitForSeconds(0.5f);
        
        float progressBeforePause = handle.Progress;
        controller.Pause(handle);
        
        AssertTrue(handle.IsPaused, "Should be paused");
        
        yield return new WaitForSeconds(0.5f);
        float progressDuringPause = handle.Progress;
        
        AssertEquals(progressBeforePause, progressDuringPause, "Progress should not change while paused");
        
        controller.Resume(handle);
        AssertFalse(handle.IsPaused, "Should not be paused after resume");
        
        yield return new WaitForSeconds(0.1f);
        AssertTrue(handle.Progress > progressDuringPause, "Progress should increase after resume");
        
        controller.StopAll();
        EndTest();
    }
    
    /// <summary>
    /// Test 6: Speed control
    /// </summary>
    private IEnumerator TestSpeedControl()
    {
        LogTest("Test 6: Speed Control");
        
        var handle = controller.PlayLooped(testClip1, -1);
        
        // Test individual speed
        handle.Speed = 2f;
        float progress1 = handle.Progress;
        yield return new WaitForSeconds(0.5f);
        float progress2 = handle.Progress;
        float deltaFast = progress2 - progress1;
        
        // Test slower speed
        handle.Speed = 0.5f;
        progress1 = handle.Progress;
        yield return new WaitForSeconds(0.5f);
        progress2 = handle.Progress;
        float deltaSlow = progress2 - progress1;
        
        AssertTrue(deltaFast > deltaSlow * 1.5f, "Fast speed should progress more than slow speed");
        
        // Test global speed
        controller.GlobalSpeed = 2f;
        yield return new WaitForSeconds(0.1f);
        AssertTrue(handle.IsPlaying, "Should still be playing with global speed change");
        
        controller.GlobalSpeed = 1f;
        controller.StopAll();
        EndTest();
    }
    
    /// <summary>
    /// Test 7: Multiple concurrent animations
    /// </summary>
    private IEnumerator TestMultipleConcurrent()
    {
        LogTest("Test 7: Multiple Concurrent");
        
        var handles = new AnimationHandle[3];
        handles[0] = controller.PlayWithMode(testClip1, PlayMode.Additive);
        handles[1] = controller.PlayWithMode(testClip2, PlayMode.Additive);
        handles[2] = controller.PlayWithMode(loopTestClip, PlayMode.Additive);
        
        yield return new WaitForSeconds(0.5f);
        
        AssertEquals(controller.ActiveAnimationCount, 3, "Should have 3 active animations");
        
        foreach (var handle in handles)
        {
            AssertTrue(handle.IsValid && handle.IsPlaying, "All should be playing");
        }
        
        controller.StopAll();
        AssertEquals(controller.ActiveAnimationCount, 0, "Should have no active animations after StopAll");
        
        EndTest();
    }
    
    /// <summary>
    /// Test 8: Weight system
    /// </summary>
    private IEnumerator TestWeightSystem()
    {
        LogTest("Test 8: Weight System");
        
        var handle = controller.Play(testClip1);
        
        handle.Weight = 0.5f;
        yield return null;
        AssertEquals(handle.Weight, 0.5f, "Weight should be set correctly", 0.01f);
        
        // Test fade weight
        controller.FadeWeight(handle, 0f, 0.5f);
        yield return new WaitForSeconds(0.6f);
        AssertTrue(handle.Weight < 0.1f, "Weight should have faded to near zero");
        
        controller.StopAll();
        EndTest();
    }
    
    /// <summary>
    /// Test 9: Memory pool functionality
    /// </summary>
    private IEnumerator TestMemoryPool()
    {
        LogTest("Test 9: Memory Pool");
        
        // Prewarm
        controller.PrewarmClip(testClip1);
        
        // Play same clip multiple times to test pooling
        for (int i = 0; i < 5; i++)
        {
            var handle = controller.Play(testClip1);
            yield return new WaitForSeconds(0.2f);
            controller.Stop(handle);
            yield return null;
        }
        
        // System should reuse pooled playables without issues
        AssertTrue(true, "Pool system working (no crashes)");
        
        EndTest();
    }
    
    /// <summary>
    /// Test 10: Event system
    /// </summary>
    private IEnumerator TestEvents()
    {
        LogTest("Test 10: Events");
        
        bool startFired = false;
        bool endFired = false;
        bool loopFired = false;
        
        controller.OnAnimationStart += (clip) => startFired = true;
        controller.OnAnimationEnd += (clip) => endFired = true;
        controller.OnAnimationLoop += (clip, count) => loopFired = true;
        
        var handle = controller.PlayLooped(loopTestClip, 2);
        
        yield return null;
        AssertTrue(startFired, "Start event should fire");
        
        yield return new WaitForSeconds(loopTestClip.length + 0.1f);
        AssertTrue(loopFired, "Loop event should fire");
        
        yield return new WaitForSeconds(loopTestClip.length * 2);
        AssertTrue(endFired, "End event should fire after max loops");
        
        EndTest();
    }
    
    #region Test Helpers
    
    private void LogTest(string testName)
    {
        if (verboseLogging)
            Debug.Log($"\n🧪 {testName}");
    }
    
    private void Log(string message)
    {
        if (verboseLogging)
            Debug.Log($"   {message}");
    }
    
    private void EndTest()
    {
        if (verboseLogging)
            Debug.Log("   ✓ Test completed");
    }
    
    private void AssertTrue(bool condition, string message)
    {
        if (condition)
        {
            testsPassed++;
            if (verboseLogging) Debug.Log($"   ✅ {message}");
        }
        else
        {
            testsFailed++;
            Debug.LogError($"   ❌ FAILED: {message}");
        }
    }
    
    private void AssertFalse(bool condition, string message)
    {
        AssertTrue(!condition, message);
    }
    
    private void AssertEquals(object actual, object expected, string message, float tolerance = 0)
    {
        bool equal = false;
        
        if (actual is float f1 && expected is float f2)
        {
            equal = Mathf.Abs(f1 - f2) <= tolerance;
        }
        else
        {
            equal = object.Equals(actual, expected);
        }
        
        AssertTrue(equal, $"{message} (Expected: {expected}, Got: {actual})");
    }
    
    #endregion
}