#nullable enable

using System;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 运行时光参数（唯一产出点）：只喂现有的 <c>specular</c>（angle/value）与 <c>backdrop</c>（value）参数，
/// 不新增材质/合成分支。静态 <see cref="ResolvedSurface.Fingerprint"/> 不参与时间与光标。
/// </summary>
public readonly record struct SurfaceLightState(float Angle, float Intensity, float Warmth)
{
    /// <summary>位图缓存键片段（含插值后的光参数；不含秒级噪声）。</summary>
    public string CacheToken => $"{Angle:F1}:{Intensity:F2}:{Warmth:F3}";
}

/// <summary>
/// 预计算 + 参数插值的轻量光模型（UI 侧 CPU，无引擎依赖）：
/// 15 分钟粒度查表（<see cref="BuildTable"/>）→ 按当前分钟线性插值 → 光标横向位置小幅偏转。
/// </summary>
public static class SurfaceLight
{
    private const int StepMinutes = 15;
    private const int SlotsPerDay = 24 * 60 / StepMinutes;

    /// <summary>关键帧（本地时刻 → 角度/强度/暖度）。角度按最短弧插值。</summary>
    private static readonly (int MinuteOfDay, float Angle, float Intensity, float Warmth)[] Keyframes =
    {
        (0, 250f, 0.25f, 0.05f),
        (6 * 60, 20f, 0.55f, 0.70f),
        (9 * 60, 45f, 0.85f, 0.35f),
        (12 * 60, 80f, 1.00f, 0.10f),
        (15 * 60, 110f, 0.85f, 0.30f),
        (18 * 60, 150f, 0.60f, 0.80f),
        (22 * 60, 200f, 0.35f, 0.20f),
    };

    private static readonly Lazy<SurfaceLightState[]> Table = new(BuildTable);

    /// <summary>取当前时点的运行光参数。</summary>
    /// <param name="localTime">本地时刻（调用方应按分钟量化，避免每秒重绘）。</param>
    /// <param name="cursorX">光标在面板内的横向位置（像素；0 或缺省关闭偏转）。</param>
    /// <param name="width">面板宽度（像素；&lt;=0 关闭偏转）。</param>
    public static SurfaceLightState Resolve(DateTime localTime, int cursorX = 0, int width = 0)
    {
        var minuteOfDay = localTime.Hour * 60 + localTime.Minute;
        var table = Table.Value;

        var slot = minuteOfDay / StepMinutes;
        var fraction = (minuteOfDay % StepMinutes) / (float)StepMinutes;
        var current = table[slot];
        var next = table[(slot + 1) % SlotsPerDay];

        var angle = LerpAngle(current.Angle, next.Angle, fraction);
        var intensity = current.Intensity + (next.Intensity - current.Intensity) * fraction;
        var warmth = current.Warmth + (next.Warmth - current.Warmth) * fraction;

        if (width > 0)
        {
            var shift = Math.Clamp(cursorX * 2f / width - 1f, -1f, 1f);
            angle = Normalize(angle + shift * 12f);
            intensity = Math.Clamp(intensity + shift * 0.08f, 0.15f, 1.25f);
        }

        return new SurfaceLightState(angle, intensity, warmth);
    }

    /// <summary>预计算：把关键帧展开成 15 分钟槽位的表（启动一次，之后只插值）。</summary>
    private static SurfaceLightState[] BuildTable()
    {
        var table = new SurfaceLightState[SlotsPerDay];
        for (var slot = 0; slot < SlotsPerDay; slot++)
        {
            table[slot] = InterpolateKeyframes(slot * StepMinutes);
        }

        return table;
    }

    private static SurfaceLightState InterpolateKeyframes(int minuteOfDay)
    {
        var index = Keyframes.Length - 1;
        for (var i = 0; i < Keyframes.Length; i++)
        {
            if (Keyframes[i].MinuteOfDay <= minuteOfDay) index = i;
            else break;
        }

        var current = Keyframes[index];
        var next = Keyframes[(index + 1) % Keyframes.Length];
        var span = (next.MinuteOfDay - current.MinuteOfDay + 24 * 60) % (24 * 60);
        if (span <= 0) span = 24 * 60;
        var fraction = (minuteOfDay - current.MinuteOfDay + 24 * 60) % (24 * 60) / (float)span;

        return new SurfaceLightState(
            LerpAngle(current.Angle, next.Angle, fraction),
            current.Intensity + (next.Intensity - current.Intensity) * fraction,
            current.Warmth + (next.Warmth - current.Warmth) * fraction);
    }

    private static float LerpAngle(float from, float to, float fraction)
    {
        var delta = (to - from) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return Normalize(from + delta * fraction);
    }

    private static float Normalize(float angle) => (angle % 360f + 360f) % 360f;
}
