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
