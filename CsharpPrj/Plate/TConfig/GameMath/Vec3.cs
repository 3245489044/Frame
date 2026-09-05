using System;

namespace TConfig.GameMath;

/// <summary>
/// 共享三维向量基元。原定义位于 BasicMethod/ECS/ECSFrame.cs（ECS.Core），
/// 为消除跨层依赖倒置，归位到 TConfig 基元层。
/// </summary>
public struct Vec3
{
    private const float Epsilon = 0.0001f;
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 One => new(1, 1, 1);
    public static Vec3 Right => new(1, 0, 0);
    public static Vec3 Left => new(-1, 0, 0);
    public static Vec3 Up => new(0, 1, 0);
    public static Vec3 Down => new(0, -1, 0);
    public static Vec3 Forward => new(0, 0, 1);
    public static Vec3 Backward => new(0, 0, -1);
    public float X, Y, Z;
    public Vec3(float x, float y, float z) => (X, Y, Z) = (x, y, z);
    public Vec3 Normalized()
    {
        float length = (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        return length > Epsilon ? new Vec3(X / length, Y / length, Z / length) : Zero;
    }
    public static bool operator ==(Vec3 a, Vec3 b)
    {
        bool XEqual = float.IsNaN(a.X) ? float.IsNaN(b.X) : Math.Abs(a.X - b.X) < Epsilon;
        bool YEqual = float.IsNaN(a.Y) ? float.IsNaN(b.Y) : Math.Abs(a.Y - b.Y) < Epsilon;
        bool ZEqual = float.IsNaN(a.Z) ? float.IsNaN(b.Z) : Math.Abs(a.Z - b.Z) < Epsilon;
        return XEqual && YEqual && ZEqual;
    }
    public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);
    public override bool Equals(object? obj) => obj is Vec3 vec && this == vec;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 v) => new(-v.X, -v.Y, -v.Z);
    public static Vec3 operator *(Vec3 v, float scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vec3 operator *(float scalar, Vec3 v) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vec3 operator /(Vec3 v, float scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar);
    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
    public static Vec3 Normalize(Vec3 v) => v.Normalized();
}
