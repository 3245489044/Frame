#nullable enable

using System;
using System.Globalization;

namespace UiBackend;

/// <summary>
/// 四边值（宽度/内缩，DIP 浮点）。参数简写按 CSS 语义展开：
/// <c>1</c> 全边；<c>1 2</c> = 上/下、左/右；<c>1 2 3</c> = 上、左/右、下；<c>1 2 3 4</c> = 上、右、下、左。
/// </summary>
public readonly record struct BorderEdgeSet(float Left, float Top, float Right, float Bottom)
{
    /// <summary>零值（不画边）。</summary>
    public static readonly BorderEdgeSet Zero = new(0, 0, 0, 0);

    /// <summary>四边同宽。</summary>
    public static BorderEdgeSet Uniform(float width) => new(width, width, width, width);

    /// <summary>四边是否都为 0（≤0 视为 0）。</summary>
    public bool IsZero => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;

    /// <summary>四边最大值。</summary>
    public float Max => Math.Max(Math.Max(Left, Top), Math.Max(Right, Bottom));

    /// <summary>四边是否同宽。</summary>
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    /// <summary>解析 CSS 简写（1–4 个非负数；失败 ⇒ false，不静默回落）。</summary>
    /// <param name="text">空白分隔的数值文本。</param>
    /// <param name="edges">展开后的四边值。</param>
    public static bool TryParseShorthand(string? text, out BorderEdgeSet edges)
    {
        edges = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return false;

        var values = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < 0) return false;
        }

        edges = parts.Length switch
        {
            1 => Uniform(values[0]),
            2 => new BorderEdgeSet(values[1], values[0], values[1], values[0]),
            3 => new BorderEdgeSet(values[1], values[0], values[1], values[2]),
            _ => new BorderEdgeSet(values[3], values[0], values[1], values[2]),
        };
        return true;
    }
}
