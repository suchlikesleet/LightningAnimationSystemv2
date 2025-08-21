using Unity.Mathematics;
using Unity.Collections;

namespace LightningAnimation
{
    /// <summary>
    /// SIMD-optimized math operations for animation processing
    /// </summary>
    public static class AnimationMath
    {
        /// <summary>
        /// Batch lerp for multiple float values
        /// </summary>
        public static void BatchLerp(ref float4 current, float4 target, float4 speed, float deltaTime)
        {
            float4 t = math.saturate(speed * deltaTime);
            current = math.lerp(current, target, t);
        }
        
        /// <summary>
        /// Batch update times for 4 animations at once
        /// </summary>
        public static float4 BatchUpdateTime(float4 currentTimes, float4 speeds, float deltaTime)
        {
            return math.mad(speeds, deltaTime, currentTimes);
        }
        
        /// <summary>
        /// Batch normalize times
        /// </summary>
        public static float4 BatchNormalizeTime(float4 times, float4 lengths)
        {
            // Avoid division by zero
            float4 safeLengths = math.max(lengths, 0.0001f);
            return times / safeLengths;
        }
        
        /// <summary>
        /// Batch check if animations completed
        /// </summary>
        public static bool4 BatchCheckCompletion(float4 normalizedTimes)
        {
            return normalizedTimes >= 1f;
        }
        
        /// <summary>
        /// Batch mod for looping
        /// </summary>
        public static float4 BatchModTime(float4 times, float4 lengths)
        {
            float4 safeLengths = math.max(lengths, 0.0001f);
            return math.fmod(times, safeLengths);
        }
        
        /// <summary>
        /// Batch weight interpolation with different blend speeds
        /// </summary>
        public static float4 BatchInterpolateWeights(float4 current, float4 target, float4 blendSpeeds, float deltaTime)
        {
            float4 maxDelta = blendSpeeds * deltaTime;
            return MoveTowards(current, target, maxDelta);
        }
        
        /// <summary>
        /// Vector MoveTowards implementation
        /// </summary>
        public static float4 MoveTowards(float4 current, float4 target, float4 maxDelta)
        {
            float4 delta = target - current;
            float4 absDelta = math.abs(delta);
            float4 sign = math.sign(delta);
            float4 step = math.min(absDelta, maxDelta);
            return current + sign * step;
        }
        
        /// <summary>
        /// Batch ease in-out interpolation
        /// </summary>
        public static float4 BatchEaseInOut(float4 t)
        {
            return t * t * (3f - 2f * t);
        }
        
        /// <summary>
        /// Fast bit count for slot occupancy
        /// </summary>
        public static int CountBits(byte value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }
        
        /// <summary>
        /// Find first zero bit (for slot allocation)
        /// </summary>
        public static int FindFirstZeroBit(byte value)
        {
            byte mask = 1;
            for (int i = 0; i < 8; i++)
            {
                if ((value & mask) == 0)
                    return i;
                mask <<= 1;
            }
            return -1;
        }
        
        /// <summary>
        /// Pack multiple booleans into flags
        /// </summary>
        public static int PackFlags(bool flag0, bool flag1, bool flag2, bool flag3)
        {
            return (flag0 ? 1 : 0) |
                   (flag1 ? 2 : 0) |
                   (flag2 ? 4 : 0) |
                   (flag3 ? 8 : 0);
        }
        
        /// <summary>
        /// Unpack flags to bool4
        /// </summary>
        public static bool4 UnpackFlags(int flags)
        {
            return new bool4(
                (flags & 1) != 0,
                (flags & 2) != 0,
                (flags & 4) != 0,
                (flags & 8) != 0
            );
        }
        
        /// <summary>
        /// Batch clamp to range
        /// </summary>
        public static float4 BatchClamp(float4 values, float4 min, float4 max)
        {
            return math.clamp(values, min, max);
        }
        
        /// <summary>
        /// Optimized lerp for animation blending
        /// </summary>
        public static float BlendWeight(float weight1, float weight2, float blend)
        {
            return math.lerp(weight1, weight2, blend);
        }
        
        /// <summary>
        /// Calculate crossfade weights
        /// </summary>
        public static float2 CrossfadeWeights(float progress)
        {
            float fadeOut = 1f - progress;
            float fadeIn = progress;
            return new float2(fadeOut, fadeIn);
        }
        
        /// <summary>
        /// Smooth step for transitions
        /// </summary>
        public static float SmoothStep(float from, float to, float t)
        {
            t = math.saturate(t);
            t = t * t * (3f - 2f * t);
            return math.lerp(from, to, t);
        }
        
        /// <summary>
        /// Calculate loop progress
        /// </summary>
        public static void GetLoopInfo(float time, float length, out int loopCount, out float loopProgress)
        {
            if (length <= 0)
            {
                loopCount = 0;
                loopProgress = 0;
                return;
            }
            
            loopCount = (int)(time / length);
            loopProgress = math.fmod(time, length) / length;
        }
    }
}