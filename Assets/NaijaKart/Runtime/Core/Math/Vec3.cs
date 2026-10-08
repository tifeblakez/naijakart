using System;

namespace NaijaKart.Core.Math
{
    /// <summary>
    /// Minimal 3D vector used by the engine-agnostic simulation. Y is up. The Unity layer converts
    /// to and from UnityEngine.Vector3 at the boundary (see NaijaKart.Unity.CoreConversions).
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0f, 0f, 0f);
        public static Vec3 Up => new Vec3(0f, 1f, 0f);
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);
        public static Vec3 Right => new Vec3(1f, 0f, 0f);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)System.Math.Sqrt(SqrMagnitude);

        /// <summary>Length of the vector projected onto the ground plane (XZ).</summary>
        public float FlatMagnitude => (float)System.Math.Sqrt(X * X + Z * Z);

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec3(X / m, Y / m, Z / m) : Zero;
            }
        }

        public Vec3 Flat => new Vec3(X, 0f, Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;
        public static float FlatDistance(Vec3 a, Vec3 b) => (a - b).FlatMagnitude;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * MathUtil.Clamp01(t);

        /// <summary>Unit vector on the ground plane for a yaw angle in radians (0 = +Z, PI/2 = +X).</summary>
        public static Vec3 FromYaw(float yawRadians) =>
            new Vec3((float)System.Math.Sin(yawRadians), 0f, (float)System.Math.Cos(yawRadians));

        /// <summary>Yaw angle in radians of this vector's ground-plane direction.</summary>
        public float ToYaw() => (float)System.Math.Atan2(X, Z);

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);
        public override string ToString() => $"({X:0.00}, {Y:0.00}, {Z:0.00})";
    }
}
