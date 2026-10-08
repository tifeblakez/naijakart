using System;

namespace NaijaKart.Core.Math
{
    public static class MathUtil
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = Pi * 2f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Inverse lerp, safe when a == b.</summary>
        public static float InverseLerp(float a, float b, float v) =>
            System.Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (System.Math.Abs(target - current) <= maxDelta)
            {
                return target;
            }
            return current + System.Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Wraps an angle in radians into [-PI, PI].</summary>
        public static float WrapAngle(float radians)
        {
            radians %= TwoPi;
            if (radians > Pi) radians -= TwoPi;
            if (radians < -Pi) radians += TwoPi;
            return radians;
        }

        public static float Sign(float v) => v < 0f ? -1f : (v > 0f ? 1f : 0f);

        /// <summary>Exponential decay toward a target that is frame-rate independent.</summary>
        public static float Damp(float current, float target, float lambda, float dt) =>
            Lerp(current, target, 1f - (float)System.Math.Exp(-lambda * dt));

        /// <summary>Maps a stat on a 0..100 scale into a [min,max] gameplay range.</summary>
        public static float StatToRange(int stat0to100, float min, float max) =>
            Lerp(min, max, Clamp(stat0to100, 0, 100) / 100f);
    }
}
