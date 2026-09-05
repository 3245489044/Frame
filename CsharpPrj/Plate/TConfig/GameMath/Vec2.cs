using System;

namespace TConfig.GameMath;

/// <summary>
/// 共享二维向量基元。原定义位于 BasicMethod/ECS/ECSFrame.cs（ECS.Core），
/// 为消除跨层依赖倒置，归位到 TConfig 基元层。
/// </summary>
public struct Vec2
{
    private const float Epsilon = 0.0001f;
    public static Vec2 Zero => new(0, 0);
    public static Vec2 One => new(1, 1);
    public float X, Y;
    public Vec2(float x, float y) => (X, Y) = (x, y);
    public Vec2 Normalized()
    {
        float length = (float)Math.Sqrt(X * X + Y * Y);
        return length > Epsilon ? new Vec2(X / length, Y / length) : Zero;
    }
    public static bool operator ==(Vec2 a, Vec2 b)
    {
        bool XEqual = float.IsNaN(a.X) ? float.IsNaN(b.X) : Math.Abs(a.X - b.X) < Epsilon;
        bool YEqual = float.IsNaN(a.Y) ? float.IsNaN(b.Y) : Math.Abs(a.Y - b.Y) < Epsilon;
        return XEqual && YEqual;
    }
    public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);
    public override bool Equals(object? obj) => obj is Vec2 vec && this == vec;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);
    public static Vec2 operator *(Vec2 v, float scalar) => new(v.X * scalar, v.Y * scalar);
    public static Vec2 operator *(float scalar, Vec2 v) => new(v.X * scalar, v.Y * scalar);
    public static Vec2 operator /(Vec2 v, float scalar) => new(v.X / scalar, v.Y / scalar);
    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static Vec2 Normalize(Vec2 v) => v.Normalized();
}
