#nullable enable

using System;

namespace Share.Composition.Text;

/// <summary>
/// 段序键 —— 只承诺「可比较」，不解释含义。
/// 领域侧把自身顺序概念（缓存带 / 优先级 / 版块序）映射为 <see cref="OrderKey"/>，
/// 内核只做稳定升序，因此顺序语义（如「稳定段在前以命中前缀缓存」）留在域内。
/// </summary>
public readonly record struct OrderKey(int Value) : IComparable<OrderKey>
{
    /// <summary>缺省序（未声明 = 0）。</summary>
    public static OrderKey Zero => new(0);

    public int CompareTo(OrderKey other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
