using System;

namespace TConfig.GameMath;

/// <summary>
/// 共享 RGBA 颜色基元。原定义位于 GameEditor.VirtualMatrix/Math/Color.cs，
/// 为消除渲染层对虚拟矩阵业务模块的依赖倒置，归位到 TConfig 基元层。
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public Color(float r, float g, float b, float a = 1.0f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static Color White => new(1.0f, 1.0f, 1.0f);
    public static Color Black => new(0.0f, 0.0f, 0.0f);
    public static Color Red => new(1.0f, 0.0f, 0.0f);
    public static Color Green => new(0.0f, 1.0f, 0.0f);
    public static Color Blue => new(0.0f, 0.0f, 1.0f);
    public static Color Gray => new(0.5f, 0.5f, 0.5f);
    public static Color Clear => new(0.0f, 0.0f, 0.0f, 0.0f);

    public bool Equals(Color other)
    {
        return R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
    }

    public override bool Equals(object? obj)
    {
        return obj is Color other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(R, G, B, A);
    }

    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
