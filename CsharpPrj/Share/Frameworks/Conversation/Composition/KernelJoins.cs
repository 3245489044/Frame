using System.Collections.Generic;
using System.Linq;

namespace Share.Composition;

/// <summary>
/// 内核拼接策略（Join）—— 各槽位装配器的公共拼接原语，语义唯一收敛点。
/// 新增拼接形态在此登记，不散落到各消费方（对照 SingleImplementation）。
/// </summary>
public static class KernelJoins
{
    /// <summary>
    /// 空行拼接：按序过滤空段后以 <c>\n\n</c> 分隔。
    /// 权威语义 = <see cref="TextComposer.JoinNonEmpty"/>（提示词 / 文本 / 快照 system 段共用）。
    /// </summary>
    public static string BlankLine(IReadOnlyList<IComponent> components)
        => TextComposer.JoinNonEmpty(components.OrderBy(c => c.Order).Select(TextOf));

    /// <summary>原样首尾相接（无分隔、不过滤空段；模板填充等由消费方另行处理）。</summary>
    public static string Concat(IReadOnlyList<IComponent> components)
        => string.Concat(components.OrderBy(c => c.Order).Select(TextOf));

    /// <summary>
    /// 序列空行拼接（<c>string.Join("\n\n", …)</c> 原义）—— <b>不过滤空段</b>。
    /// 与 <see cref="BlankLine"/> 的区别：本方法保留空段（产生连续分隔），供语义要求保真的调用方使用
    /// （如 <c>PromptNodeProjector.Join</c> 的既有口径，迁移时不得"顺手修正"）。
    /// </summary>
    /// <param name="parts">片段序列（null → 空串）。</param>
    public static string JoinAll(IEnumerable<string?>? parts)
        => parts is null ? string.Empty : string.Join("\n\n", parts);

    /// <summary>取部件文本表示（null → 空串）。</summary>
    public static string TextOf(IComponent component) => component.Content?.ToString() ?? string.Empty;
}
