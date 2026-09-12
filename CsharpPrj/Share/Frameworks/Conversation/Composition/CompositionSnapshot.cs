using System;
using System.Collections.Generic;
using Share.Conversation.ContextBinding;

namespace Share.Composition;

/// <summary>
/// 组装快照 —— 每次组装的「部件集 + 逐段绑定」，与 <see cref="PromptSnapshot"/> 同源同构（Binding + SHA256）。
/// 写侧与视图（UI ≡ LLM）一致，可校验 / 回放 / 降级到事件流。
/// 哈希唯一权威 = <see cref="PromptSnapshotBuilder.Hash"/>（不另起第二份摘要算法）。
/// </summary>
/// <param name="Parts">本次装配的实际部件集（含槽位与顺序）。</param>
/// <param name="Bindings">逐段绑定（锚点键 / 来源域 / 投喂目标 / 文本哈希）。</param>
public sealed record CompositionSnapshot(
    IReadOnlyList<IComponent> Parts,
    IReadOnlyList<MessageBinding> Bindings)
{
    /// <summary>
    /// 诊断留痕（部分成功 / 未装配降级等）—— 默认空。
    /// 图组装等「幂等 + 部分成功不回滚」语义经此承载，**不入文本拼接模型**。
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    /// <summary>是否存在诊断（部分成功判定；调用方不可假定整体原子）。</summary>
    public bool HasDiagnostics => Diagnostics.Count > 0;

    /// <summary>空快照（未捕获时的兜底；视图侧视为无 system 段）。</summary>
    public static CompositionSnapshot Empty { get; } =
        new(Array.Empty<IComponent>(), Array.Empty<MessageBinding>());

    /// <summary>文本 SHA256 十六进制摘要（委托写侧快照构建器，保证与 I3 校验同一算法）。</summary>
    public static string Hash(string? text) => PromptSnapshotBuilder.Hash(text);

    /// <summary>取指定锚点的绑定（无则 null）。</summary>
    public MessageBinding? BindingFor(AnchorKey key)
    {
        foreach (var b in Bindings)
            if (b.Key == key) return b;
        return null;
    }
}
