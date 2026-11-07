#nullable enable

using System;
using System.Globalization;

namespace UiBackend;

/// <summary>
/// 四角圆角半径（DIP 浮点）。简写按 CSS 语义展开：
/// <c>1</c> 四角；<c>1 2</c> = 左上/右下、右上/左下；<c>1 2 3</c> = 左上、右上/左下、右下；<c>1 2 3 4</c> = 左上、右上、右下、左下。
/// </summary>
public readonly record struct CornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    /// <summary>零值（直角）。</summary>
    public static readonly CornerRadii Zero = new(0, 0, 0, 0);

    /// <summary>四角同半径。</summary>
    public static CornerRadii Uniform(float radius) => new(radius, radius, radius, radius);

    /// <summary>四角是否都为 0。</summary>
    public bool IsZero => TopLeft <= 0 && TopRight <= 0 && BottomRight <= 0 && BottomLeft <= 0;

    /// <summary>四角最大值。</summary>
    public float Max => Math.Max(Math.Max(TopLeft, TopRight), Math.Max(BottomRight, BottomLeft));

    /// <summary>解析 CSS 圆角简写（1–4 个非负数；失败 ⇒ false）。</summary>
    /// <param name="text">空白分隔的数值文本。</param>
    /// <param name="radii">展开后的四角值。</param>
    public static bool TryParseShorthand(string? text, out CornerRadii radii)
    {
        radii = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return false;

        var values = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < 0) return false;
        }

        radii = parts.Length switch
        {
            1 => Uniform(values[0]),
            2 => new CornerRadii(values[0], values[1], values[0], values[1]),
            3 => new CornerRadii(values[0], values[1], values[2], values[1]),
            _ => new CornerRadii(values[0], values[1], values[2], values[3]),
        };
        return true;
    }
}
