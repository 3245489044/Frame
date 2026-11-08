#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 表面纯数学（**载体无关、BCL-only**）：相位/角度归一。
/// <para>宿主前端不得再各自写取模：0..1 相位与 -180..180 角度差只在这里实现一次。</para>
/// </summary>
public static class SurfaceMath
{
    /// <summary>归一到一个周期 [0,1)（相位、周长参数 t）。</summary>
    public static float Wrap01(float value) => value - MathF.Floor(value);

    /// <summary>角度差归一（[-180,180]）：用于色相误差判定。</summary>
    public static double WrapDegrees180(double degrees)
    {
        var value = degrees % 360.0;
        if (value > 180.0) value -= 360.0;
        if (value < -180.0) value += 360.0;
        return value;
    }
}
