using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 引用视图（投影结果的中立契约）—— 当前执行上下文里「累积历史告诉我们的」：作用域锚 + 挂载节点 + 时序序列。
/// 只读、LLM-free；供下轮上下文组装（provide_prompt / cline_execute）消费。
/// </summary>
public interface IRefView
{
    /// <summary>当前作用域锚（如 taskKey / historyId）。</summary>
    string? ScopeKey { get; }

    /// <summary>该作用域历史上挂载的节点键集（History/Task 归拢的节点）。</summary>
    IReadOnlyList<string> NodeKeys { get; }

    /// <summary>按键内 (round, seq) 折叠出的当前视图序列（round_sequence 边已退役，A4′）。</summary>
    IReadOnlyList<string> Sequence { get; }
}
