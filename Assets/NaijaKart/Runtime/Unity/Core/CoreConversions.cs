using NaijaKart.Core.Math;
using UnityEngine;

namespace NaijaKart.Unity
{
    /// <summary>Boundary conversions between the engine-agnostic core and UnityEngine types.</summary>
    public static class CoreConversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion HeadingToRotation(float headingRadians) => Quaternion.Euler(0f, headingRadians * Mathf.Rad2Deg, 0f);
    }
}
