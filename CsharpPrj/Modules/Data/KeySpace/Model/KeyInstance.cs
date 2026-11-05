using System.Globalization;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Model;

/// <summary>
/// 键实例（族 × 面 × 坐标）：键的**唯一内存形态**，字符串只是它在某一面的投影。
/// <para>
/// 纪律：实例不可变（<see cref="With(KeyAxis,string?)"/>/<see cref="At"/> 返回新实例）；
/// 值袋缺席与空串不混同（空值不入袋）；相等性按内容（族/面/值袋），不依赖底层字典引用。
/// </para>
/// </summary>
public readonly record struct KeyInstance(string Family, KeySurface Surface, KeyPoint Point)
{
    /// <summary>族根（默认存储面；坐标为空）。</summary>
    public static KeyInstance Root(string familyId, KeySurface surface = KeySurface.Storage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        return new KeyInstance(familyId, surface, KeyPoint.Empty);
    }

    /// <summary>是否空实例。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Family);

    /// <summary>换面（坐标不变）。</summary>
    public KeyInstance At(KeySurface surface) => this with { Surface = surface };

    /// <summary>换族（坐标不变；身份→位置派生用）。</summary>
    public KeyInstance InFamily(string familyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        return this with { Family = familyId, Point = new KeyPoint(familyId, Point.Values) };
    }

    /// <summary>挂轴值（空值 = 移除该轴，保持「缺席 ≠ 空串」）。</summary>
    public KeyInstance With(KeyAxis axis, string? value)
    {
        if (axis.IsEmpty) throw new ArgumentException("键轴名为空", nameof(axis));

        var values = new List<(string Name, string Value)>();
        foreach (var pair in Point.Values)
        {
            if (!string.Equals(pair.Key, axis.Name, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(pair.Value))
            {
                values.Add((pair.Key, pair.Value));
            }
        }

        if (!string.IsNullOrEmpty(value))
        {
            values.Add((axis.Name!, value!));
        }

        return this with { Point = KeyPoint.Of(Family, values.ToArray()) };
    }

    /// <summary>挂轴值（整数，Invariant 文本）。</summary>
    public KeyInstance With(KeyAxis axis, int value) => With(axis, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>挂轴值（长整数，Invariant 文本）。</summary>
    public KeyInstance With(KeyAxis axis, long value) => With(axis, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>取轴值（缺席 ⇒ null）。</summary>
    public string? Value(KeyAxis axis)
        => !axis.IsEmpty && Point.Values.TryGetValue(axis.Name!, out var value) ? value : null;

    /// <summary>轴值 → 整数（缺席 / 非纯数字 / 溢出 ⇒ false；口径唯一 = <see cref="KeyValues.TryParseInt"/>）。</summary>
    public bool TryGetInt(KeyAxis axis, out int value)
        => KeyValues.TryParseInt(Value(axis), out value);

    /// <summary>轴值 → 长整数（缺席 / 非纯数字 / 溢出 ⇒ false；口径唯一 = <see cref="KeyValues.TryParseLong"/>）。</summary>
    public bool TryGetLong(KeyAxis axis, out long value)
        => KeyValues.TryParseLong(Value(axis), out value);

    /// <summary>值袋快照（供代数求值；不复制字典）。</summary>
    public KeyPoint ToPoint() => Point;

    public bool Equals(KeyInstance other)
    {
        if (!string.Equals(Family, other.Family, StringComparison.Ordinal)) return false;
        if (Surface != other.Surface) return false;
        if (Point.Values.Count != other.Point.Values.Count) return false;

        foreach (var pair in Point.Values)
        {
            if (!other.Point.Values.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Family, StringComparer.Ordinal);
        hash.Add(Surface);
        foreach (var pair in Point.Values.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
        => IsEmpty ? "(empty)" : $"{Family}#{Surface}";
}
