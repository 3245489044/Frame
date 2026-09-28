using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 声明流运行结果（**框架镜像**）—— 把「已声明的一条 [Flow] 跑一次」的结论面与 Agent 侧工作流结果类型解耦，
/// 使 Frame 通用执行器无需反向依赖应用层程序集（G-3 单向依赖）。
/// <para>
/// <see cref="Outputs"/> 为**读投影类流**（S4-a）的产物回收面：簿记流（写侧）只需结论 ⇒ 恒 null，零改动；
/// 读投影流（如 <c>context-projection</c>）需把节点产物交回调用方 ⇒ 由执行器自 <c>NodeRoundResult.Records</c> 收割。
/// </para>
/// </summary>
/// <param name="Ok">是否成功。</param>
/// <param name="Error">失败归因（Ok=true 时为空）。</param>
/// <param name="ErrorStep">失败步骤（诊断可见化用）。</param>
/// <param name="ErrorClass">失败类别（诊断可见化用）。</param>
public readonly record struct DeclaredFlowRunResult(bool Ok, string? Error, string? ErrorStep, string? ErrorClass)
{
    /// <summary>本轮节点执行记录（读投影流回收产物用；簿记流 / 失败 ⇒ null）。</summary>
    public IReadOnlyList<NodeExecutionRecord>? Outputs { get; init; }

    /// <summary>成功结论。</summary>
    public static DeclaredFlowRunResult Committed() => new(true, null, null, null);

    /// <summary>成功结论 + 产物回收（读投影类流）。</summary>
    /// <param name="outputs">本轮节点执行记录。</param>
    public static DeclaredFlowRunResult Committed(IReadOnlyList<NodeExecutionRecord>? outputs)
        => new(true, null, null, null) { Outputs = outputs };

    /// <summary>失败结论（归因逐字携带，供诊断可见化）。</summary>
    public static DeclaredFlowRunResult Failed(string? error, string? errorStep, string? errorClass)
        => new(false, error, errorStep, errorClass);
}
