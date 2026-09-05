using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 终答归集策略（可注入）：从执行记录 + 轮内数据空间快照推导一轮终答文本。
/// Agent 侧实现复用框架 <see cref="Operation.HierarchyMethod.Nodes.Relations.WorkflowResultDeriver"/>
/// 的链尾索引 + 应用侧取文本（ExecResultDocument / safe_output / 启发式兜底）。
/// 未装配 → 编排器产出空终答（仅记录执行快照，零 LLM）。
/// </summary>
public interface IFinalAnswerPolicy
{
    /// <summary>推导一轮终答文本；无可用终答返回 null/空。</summary>
    string? ResolveFinalAnswer(
        RoundSpec spec,
        IReadOnlyList<NodeExecutionRecord> records,
        IReadOnlyDictionary<string, object?> vars);
}
