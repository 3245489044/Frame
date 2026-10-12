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

    /// <summary>mark 具名 token 集归一（大小写不敏感；分隔符 <c>|</c>/<c>,</c>/<c>;</c>；
    /// 空 = None；含未识别 token → null = 拒绝，不静默降级）。</summary>
    /// <param name="mark">原始标记（可为 token 集，如 <c>Terminal|Port</c>）。</param>
    public string? Normalize(string? mark)
    {
        var text = (mark ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return None;
        }

        var parts = text.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new System.Collections.Generic.List<string>();
        foreach (var part in parts)
        {
            var hit = _marks.FirstOrDefault(m => part.Equals(m, StringComparison.OrdinalIgnoreCase));
            if (hit is null) return null;
            if (!seen.Contains(hit)) seen.Add(hit);
        }

        return seen.Count == 0 ? None : string.Join("|", seen);
    }
}
