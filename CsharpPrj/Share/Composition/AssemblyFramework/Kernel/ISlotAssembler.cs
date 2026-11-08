using System.Collections.Generic;

namespace Share.Composition;

/// <summary>
/// 叶子装配器 —— 承载单槽领域逻辑（过滤 → 选择 → 装配），保持纯函数、可单测。
/// 与内核其它部分的职责边界：<b>槽位策略</b>（<see cref="SlotSpec"/>）管「何时拼、怎么拼」，
/// <b>叶子装配器</b>管「本槽的内容怎么算出来」。
/// </summary>
/// <typeparam name="TCtx">领域装配上下文（须实现 <see cref="IAssemblyContext"/>）。</typeparam>
public interface ISlotAssembler<in TCtx> where TCtx : IAssemblyContext
{
    /// <summary>本装配器负责的槽位标识。</summary>
    string Slot { get; }

    /// <summary>装配单槽（候选部件 → 槽文本；无内容返回空串，由调用方按空段剔除）。</summary>
    string Assemble(TCtx ctx, IReadOnlyList<IComponent> candidates);
}

/// <summary>
/// 文本槽默认装配器 —— 按 <see cref="SlotSpec"/> 的 Join 策略（缺省空行）装配候选部件。
/// 无额外领域过滤时可直接使用（<c>new TextSlotAssembler("L0")</c>）。
/// </summary>
public sealed class TextSlotAssembler : ISlotAssembler<IAssemblyContext>
{
    private readonly SlotSpec _spec;

    /// <param name="slot">槽位标识。</param>
    /// <param name="when">激活条件（null = 恒激活）。</param>
    /// <param name="join">拼接策略（null = <see cref="KernelJoins.BlankLine"/>）。</param>
    public TextSlotAssembler(string slot, System.Func<IAssemblyContext, bool>? when = null,
        System.Func<IReadOnlyList<IComponent>, string>? join = null)
        => _spec = new SlotSpec(slot, when, join);

    /// <inheritdoc />
    public string Slot => _spec.Name;

    /// <inheritdoc />
    public string Assemble(IAssemblyContext ctx, IReadOnlyList<IComponent> candidates)
        => _spec.Active(ctx) ? _spec.JoinOf(candidates) : string.Empty;
}
