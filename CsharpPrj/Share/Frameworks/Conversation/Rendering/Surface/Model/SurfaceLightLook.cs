#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 引擎光参数的 look 子集（声明值视图）：太阳方位/仰角/强度/环境/色温/底色。
/// <para>真源 = <c>appearance.ushar</c> 的 <c>light_*</c> 令牌；UI 侧只组装 JSON，引擎侧解析。
/// 缺省（<see cref="Empty"/>）用 NaN 表示「未声明 ⇒ 由引擎按覆盖区派生」，不写第二套值。</para>
/// </summary>
public sealed record SurfaceLightLook(
    float SunAzimuthDegrees,
    float SunElevationDegrees,
    float SunIntensity,
    float Ambient,
    float Warmth,
    int TintArgb)
{
    /// <summary>未声明（全 NaN / 底色 0 ⇒ 引擎按覆盖区派生）。</summary>
    public static readonly SurfaceLightLook Empty = new(
        float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 0);

    /// <summary>是否未声明。</summary>
    public bool IsEmpty => !float.IsFinite(SunAzimuthDegrees)
                           && !float.IsFinite(SunElevationDegrees)
                           && !float.IsFinite(SunIntensity)
                           && !float.IsFinite(Ambient)
                           && !float.IsFinite(Warmth)
                           && TintArgb == 0;

    /// <summary>序列化为引擎选项 JSON（只写已声明字段；NaN/0 省略 ⇒ 引擎保持派生）。</summary>
    public string ToJson()
    {
        var builder = new StringBuilder(160);
        builder.Append('{');
        var first = true;
        AppendFloat(builder, ref first, "sunAzimuthDegrees", SunAzimuthDegrees);
        AppendFloat(builder, ref first, "sunElevationDegrees", SunElevationDegrees);
        AppendFloat(builder, ref first, "sunIntensity", SunIntensity);
        AppendFloat(builder, ref first, "ambient", Ambient);
        AppendFloat(builder, ref first, "warmth", Warmth);
        if (TintArgb != 0)
        {
            AppendName(builder, ref first, "tintArgb");
            builder.Append(TintArgb.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendFloat(StringBuilder builder, ref bool first, string name, float value)
    {
        if (!float.IsFinite(value)) return;
        AppendName(builder, ref first, name);
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AppendName(StringBuilder builder, ref bool first, string name)
    {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('"').Append(name).Append("\":");
    }
}
