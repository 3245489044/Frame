#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 计划上下文 —— 适用性/取值的只读事实面（键值 + 开关）。
/// <para>
/// 刻意采用<b>非泛型</b>键值面（缺省常量 + 任意键）：泛型上下文会把类型参数传染到
/// 注册表/计划/装配/快照全部类型，使不同领域的段无法登记到同一注册表。
/// </para>
/// </summary>
public interface ISegmentContext
{
    /// <summary>取事实值（键不存在 → false；值可为 null = 显式空）。</summary>
    bool TryGet(string key, out object? value);

    /// <summary>开关是否启用（未声明开关集 → 恒启用）。</summary>
    bool IsEnabled(string toggle);
}

/// <summary>键值上下文默认实现（纯数据、不可变）。</summary>
public sealed class SegmentContext : ISegmentContext
{
    private readonly IReadOnlyDictionary<string, object?> _values;
    private readonly IReadOnlySet<string>? _toggles;

    /// <param name="values">事实值（null → 空）。</param>
    /// <param name="toggles">启用的开关集（null → 全部启用，不做开关过滤）。</param>
    public SegmentContext(
        IReadOnlyDictionary<string, object?>? values = null,
        IEnumerable<string>? toggles = null)
    {
        _values = values ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        _toggles = toggles is null
            ? null
            : new HashSet<string>(toggles.Where(static t => !string.IsNullOrWhiteSpace(t)), StringComparer.Ordinal);
    }

    /// <summary>空上下文。</summary>
    public static SegmentContext Empty { get; } = new();

    /// <inheritdoc />
    public bool TryGet(string key, out object? value)
    {
        if (!string.IsNullOrWhiteSpace(key) && _values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }
        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool IsEnabled(string toggle) => _toggles is null || _toggles.Contains(toggle);

    /// <summary>按 <see cref="TextStateMap"/> 取某键的段状态（别名解析等判据共用）。</summary>
    public TextState StateOf(string key, BlankSemantics blankAs = BlankSemantics.Value)
        => TextStateMap.Map(TryGet(key, out var value), value, blankAs);
}
