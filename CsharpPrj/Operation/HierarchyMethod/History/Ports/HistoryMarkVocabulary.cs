using System;
using System.Linq;

namespace Operation.HierarchyMethod.History.Ports;

/// <summary>
/// 入史标记词表（框架声明，宿主注入）—— mark 具名值集 + 标记→内容类别映射（唯一映射点）。
/// <para>框架内容层不持任何宿主标记字面量；标记归一/映射经本词表。</para>
/// </summary>
public sealed class HistoryMarkVocabulary
{
    private readonly string[] _marks;

    /// <summary>
    /// </summary>
    /// <param name="none">「不入史」mark 具名值（面板可见但不在历史视图）。</param>
    /// <param name="marks">全部具名值（含 <paramref name="none"/>）。</param>
    /// <param name="kindOfMark">mark → 内容类别（须 ∈ 宿主识别词表）。</param>
    public HistoryMarkVocabulary(string none, string[] marks, Func<string, string> kindOfMark)
    {
        None = none ?? throw new ArgumentNullException(nameof(none));
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
        KindOfMark = kindOfMark ?? throw new ArgumentNullException(nameof(kindOfMark));
    }

    /// <summary>「不入史」mark 具名值。</summary>
    public string None { get; }

    /// <summary>全部具名值（渲染/校验用）。</summary>
    public System.Collections.Generic.IReadOnlyList<string> Marks => _marks;

    /// <summary>mark → 内容类别映射（唯一映射点）。</summary>
    public Func<string, string> KindOfMark { get; }

    /// <summary>mark 具名值归一（大小写不敏感；未识别 → null = 拒绝，不静默降级为 None）。</summary>
    /// <param name="mark">原始标记（空 = None）。</param>
    public string? Normalize(string? mark)
    {
        var text = (mark ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return None;
        }

        return _marks.FirstOrDefault(m => text.Equals(m, StringComparison.OrdinalIgnoreCase));
    }
}
