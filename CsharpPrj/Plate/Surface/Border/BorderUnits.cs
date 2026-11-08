#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 边框单位换算（**唯一实现**）：载体无关量一律 DIP，设备像素只在绘制点换算。
/// <para>禁止取整：0.5px / 1.25px 必须保留分数，由 GDI+ 抗锯齿或 CSS 小数 px 兑现。</para>
/// </summary>
public static class BorderUnits
{
    /// <summary>DIP 基准 DPI。</summary>
    public const float BaseDpi = 96f;

    /// <summary>DPI 归一（**唯一实现**）：未取到 DPI（≤0）一律按 <see cref="BaseDpi"/>。</summary>
    /// <param name="dpi">设备 DPI（控件/窗体报告值，可能为 0）。</param>
    public static float NormalizeDpi(float dpi) => dpi <= 0f ? BaseDpi : dpi;

    /// <summary>DPI 归一（整数口径；与浮点重载同一判据）。</summary>
    /// <param name="dpi">设备 DPI（≤0 时按 96）。</param>
    public static int NormalizeDpi(int dpi) => dpi <= 0 ? (int)BaseDpi : dpi;

    /// <summary>DIP → 设备像素（浮点，不取整）。</summary>
    /// <param name="dips">逻辑像素（DIP）。</param>
    /// <param name="dpi">目标设备 DPI（≤0 时按 96）。</param>
    public static float DipsToPixels(float dips, float dpi)
        => (float)(dips * (dpi <= 0 ? BaseDpi : dpi) / BaseDpi);

    /// <summary>设备像素 → DIP（浮点，不取整）。</summary>
    /// <param name="pixels">设备像素。</param>
    /// <param name="dpi">目标设备 DPI（≤0 时按 96）。</param>
    public static float PixelsToDips(float pixels, float dpi)
        => (float)(pixels * BaseDpi / (dpi <= 0 ? BaseDpi : dpi));
}
