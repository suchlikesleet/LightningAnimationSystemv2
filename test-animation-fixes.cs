using System.Collections;
using UnityEngine;
using LightningAnimation;
using PlayMode = LightningAnimation.PlayMode;

/// <summary>
/// Comprehensive test suite for Lightning Animation System fixes
/// Tests all the critical issues identified in the diagnosis
/// </summary>
[RequireComponent(typeof(PlayableAnimationController))]
public class TestAnimationFixes : MonoBehaviour
{
    [Header("Test Clips")]
    public AnimationClip testClip1;
    public AnimationClip testClip2;
    public AnimationClip testClip3;
    
    [Header("Test Settings")]
    public bool runTests = false;
    public bool verboseLogging = true;
    
    private PlayableAnimationController controller;
    private int testsPassed = 0;
    private int testsFailed = 0;
    
    void Start()
    {
        controller = GetComponent<PlayableAnimationController>();
        
        if (runTests && controller != null)
        {
            StartCoroutine(RunAllTests());
        }
    }
    
    IEnumerator RunAllTests()
    {
        Debug.Log("=== STARTING LIGHTNING ANIMATION SYSTEM TESTS ===");
        yield return new WaitForSeconds(1f);
        
        // Test 1: Loop Count Fix
        yield return StartCoroutine(TestLoopCount());
        
        // Test 2: Queue Processing Fix
        yield return StartCoroutine(TestQueueProcessing());
        
        // Test 3: Animation Stacking Fix
        yield return StartCoroutine(TestAnimationStacking());
        
        // Test 4: Crossfade Completion Fix
        yield return StartCoroutine(TestCrossfadeCompletion());
        
        // Test 5: Speed Control Fix
        yield return StartCoroutine(TestSpeedControl());
        
        // Test 6: Graph Lifecycle Fix
        yield return StartCoroutine(TestGraphLifecycle());
        
        // Test 7: Weight Management Fix
        yield return StartCoroutine(TestWeightManagement());
        
        // Final Report
        Debug.Log("=== TEST RESULTS ===");
        Debug.Log($"Tests Passed: {testsPassed}");
        Debug.Log($"Tests Failed: {testsFailed}");
        Debug.Log($"Success Rate: {(float)testsPassed / (testsPassed + testsFailed) * 100:F1}%");
    }
    
    #region Test 1: Loop Count Fix
    IEnumerator TestLoopCount()
    {
        Debug.Log("[TEST 1] Testing Loop Count Fix...");
        
        int expectedLoops = 3;
        int actualLoops = 0;
        bool testComplete = false;
        
        // Subscribe to loop event
        controller.OnAnimationLoop += (clipName, loopCount) => 
        {
            actualLoops = loopCount;
            if (verboseLogging) Debug.Log($"Loop {loopCount} detected");
        };
        
        controller.OnAnimationEnd += (clipName) => 
        {
            testComplete = true;
        };
        
        // Play animation with 3 loops
        var handle = controller.PlayLooped(testClip1, expectedLoops);
        
        // Wait for completion
        float timeout = testClip1.length * expectedLoops + 1f;
        float elapsed = 0;
        
        while (!testComplete && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Verify results
        if (actualLoops == expectedLoops)
        {
            Debug.Log("[TEST 1] ✓ PASSED - Loop count is correct");
            testsPassed++;
        }
        else
        {
            Debug.LogError($"[TEST 1] ✗ FAILED - Expected {expectedLoops} loops, got {actualLoops}");
            testsFailed++;
        }
        
        controller.StopAll();
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 2: Queue Processing Fix
    IEnumerator TestQueueProcessing()
    {
        Debug.Log("[TEST 2] Testing Queue Processing Fix...");
        
        bool firstCompleted = false;
        bool secondPlayed = false;
        bool thirdPlayed = false;
        
        controller.OnAnimationStart += (clipName) => 
        {
            if (clipName == testClip2.name) secondPlayed = true;
            if (clipName == testClip3.name) thirdPlayed = true;
        };
        
        // Play first animation
        controller.Play(testClip1, () => firstCompleted = true);
        
        // Queue second and third
        controller.PlayWithMode(testClip2, PlayMode.Queue);
        controller.PlayWithMode(testClip3, PlayMode.Queue);
        
        // Wait for all to complete
        float timeout = (testClip1.length + testClip2.length + testClip3.length) + 2f;
        float elapsed = 0;
        
        while (elapsed < timeout && (!firstCompleted || !secondPlayed || !thirdPlayed))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Verify results
        if (firstCompleted && secondPlayed && thirdPlayed)
        {
            Debug.Log("[TEST 2] ✓ PASSED - Queue processed correctly");
            testsPassed++;
        }
        else
        {
            Debug.LogError($"[TEST 2] ✗ FAILED - Queue not processed (1st:{firstCompleted}, 2nd:{secondPlayed}, 3rd:{thirdPlayed})");
            testsFailed++;
        }
        
        controller.StopAll();
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 3: Animation Stacking Fix
    IEnumerator TestAnimationStacking()
    {
        Debug.Log("[TEST 3] Testing Animation Stacking Fix...");
        
        // Play multiple animations in additive mode
        var handle1 = controller.PlayWithMode(testClip1, PlayMode.Additive);
        yield return new WaitForSeconds(0.2f);
        
        var handle2 = controller.PlayWithMode(testClip2, PlayMode.Additive);
        yield return new WaitForSeconds(0.2f);
        
        var handle3 = controller.PlayWithMode(testClip3, PlayMode.Additive);
        yield return new WaitForSeconds(0.2f);
        
        // Stop first animation
        controller.Stop(handle1);
        yield return new WaitForSeconds(0.1f);
        
        // Check that it's properly disconnected
        bool handle1Invalid = !controller.IsPlaying(handle1);
        bool handle2Valid = controller.IsPlaying(handle2);
        bool handle3Valid = controller.IsPlaying(handle3);
        
        // Stop remaining
        controller.StopAll();
        yield return new WaitForSeconds(0.1f);
        
        // Check all are stopped
        bool allStopped = !controller.IsAnyPlaying();
        
        if (handle1Invalid && handle2Valid && handle3Valid && allStopped)
        {
            Debug.Log("[TEST 3] ✓ PASSED - Animations properly disconnected");
            testsPassed++;
        }
        else
        {
            Debug.LogError("[TEST 3] ✗ FAILED - Animation stacking/disconnection issue");
            testsFailed++;
        }
        
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 4: Crossfade Completion Fix
    IEnumerator TestCrossfadeCompletion()
    {
        Debug.Log("[TEST 4] Testing Crossfade Completion Fix...");
        
        bool fadeCompleted = false;
        float fadeTime = 0.5f;
        
        // Play first animation
        controller.Play(testClip1);
        yield return new WaitForSeconds(0.2f);
        
        // Crossfade to second
        var handle = controller.PlayWithCrossfade(testClip2, fadeTime, () => fadeCompleted = true);
        
        // Wait for fade
        yield return new WaitForSeconds(fadeTime + 0.2f);
        
        // Check weight is correct (should be 1.0)
        float weight = controller.GetWeight(handle);
        bool weightCorrect = Mathf.Abs(weight - 1f) < 0.01f;
        
        // Wait for completion
        yield return new WaitForSeconds(testClip2.length);
        
        if (weightCorrect && fadeCompleted)
        {
            Debug.Log("[TEST 4] ✓ PASSED - Crossfade completed correctly");
            testsPassed++;
        }
        else
        {
            Debug.LogError($"[TEST 4] ✗ FAILED - Crossfade issue (weight:{weight}, completed:{fadeCompleted})");
            testsFailed++;
        }
        
        controller.StopAll();
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 5: Speed Control Fix
    IEnumerator TestSpeedControl()
    {
        Debug.Log("[TEST 5] Testing Speed Control Fix...");
        
        float testSpeed = 2f;
        float globalSpeed = 0.5f;
        float expectedDuration = testClip1.length / (testSpeed * globalSpeed);
        
        // Set global speed
        controller.GlobalSpeed = globalSpeed;
        
        // Play with custom speed
        var handle = controller.Play(testClip1);
        controller.SetSpeed(handle, testSpeed);
        
        float startTime = Time.time;
        bool completed = false;
        
        controller.OnAnimationEnd += (clipName) => 
        {
            if (clipName == testClip1.name) completed = true;
        };
        
        // Wait for completion
        while (!completed && Time.time - startTime < expectedDuration + 1f)
        {
            yield return null;
        }
        
        float actualDuration = Time.time - startTime;
        float tolerance = 0.2f;
        
        if (Mathf.Abs(actualDuration - expectedDuration) < tolerance)
        {
            Debug.Log("[TEST 5] ✓ PASSED - Speed control working correctly");
            testsPassed++;
        }
        else
        {
            Debug.LogError($"[TEST 5] ✗ FAILED - Expected duration {expectedDuration:F2}s, got {actualDuration:F2}s");
            testsFailed++;
        }
        
        // Reset global speed
        controller.GlobalSpeed = 1f;
        controller.StopAll();
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 6: Graph Lifecycle Fix
    IEnumerator TestGraphLifecycle()
    {
        Debug.Log("[TEST 6] Testing Graph Lifecycle...");
        
        // Check initial state
        bool initialValid = controller.IsGraphInitialized;
        
        // Play and stop multiple times
        for (int i = 0; i < 3; i++)
        {
            controller.Play(testClip1);
            yield return new WaitForSeconds(0.1f);
            controller.StopAll();
            yield return new WaitForSeconds(0.1f);
        }
        
        // Graph should still be valid
        bool stillValid = controller.IsGraphInitialized;
        
        // Test pause/resume
        controller.Play(testClip1);
        yield return new WaitForSeconds(0.1f);
        
        var handle = controller.GetActiveHandles()[0];
        controller.Pause(handle);
        bool pauseValid = controller.IsPaused(handle);
        
        controller.Resume(handle);
        bool resumeValid = !controller.IsPaused(handle);
        
        controller.StopAll();
        
        if (initialValid && stillValid && pauseValid && resumeValid)
        {
            Debug.Log("[TEST 6] ✓ PASSED - Graph lifecycle managed correctly");
            testsPassed++;
        }
        else
        {
            Debug.LogError("[TEST 6] ✗ FAILED - Graph lifecycle issue");
            testsFailed++;
        }
        
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
    
    #region Test 7: Weight Management Fix
    IEnumerator TestWeightManagement()
    {
        Debug.Log("[TEST 7] Testing Weight Management...");
        
        // Play animation and test weight changes
        var handle = controller.Play(testClip1);
        
        // Test immediate weight set
        controller.SetWeight(handle, 0.5f);
        yield return null;
        float weight1 = controller.GetWeight(handle);
        
        // Test fade weight
        controller.FadeWeight(handle, 1f, 0.5f);
        yield return new WaitForSeconds(0.6f);
        float weight2 = controller.GetWeight(handle);
        
        // Test weight clamping
        controller.SetWeight(handle, 2f); // Should clamp to 1
        float weight3 = controller.GetWeight(handle);
        
        controller.SetWeight(handle, -1f); // Should clamp to 0
        float weight4 = controller.GetWeight(handle);
        
        controller.StopAll();
        
        bool test1 = Mathf.Abs(weight1 - 0.5f) < 0.01f;
        bool test2 = Mathf.Abs(weight2 - 1f) < 0.01f;
        bool test3 = weight3 <= 1f;
        bool test4 = weight4 >= 0f;
        
        if (test1 && test2 && test3 && test4)
        {
            Debug.Log("[TEST 7] ✓ PASSED - Weight management working correctly");
            testsPassed++;
        }
        else
        {
            Debug.LogError($"[TEST 7] ✗ FAILED - Weight issues (0.5:{test1}, fade:{test2}, clamp_max:{test3}, clamp_min:{test4})");
            testsFailed++;
        }
        
        yield return new WaitForSeconds(0.5f);
    }
    #endregion
}
