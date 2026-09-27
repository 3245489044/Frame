using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 引用视图默认实现（<see cref="IRefView"/>）：作用域锚 + 挂载节点键 + 时序序列。
/// 不可变只读快照，供 RefHead 默认投影与消费方读取。
/// </summary>
/// <param name="ScopeKey">当前作用域锚。</param>
/// <param name="NodeKeys">该作用域历史上挂载的节点键集。</param>
/// <param name="Sequence">按键内 (round, seq) 折叠出的当前视图序列。</param>
public sealed record RefView(
    string? ScopeKey,
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<string> Sequence) : IRefView;
