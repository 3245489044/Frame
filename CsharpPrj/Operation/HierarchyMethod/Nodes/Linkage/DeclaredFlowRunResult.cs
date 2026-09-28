namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 声明流运行结果（**框架镜像**）—— 把「已声明的一条 [Flow] 跑一次」的结论面与 Agent 侧工作流结果类型解耦，
/// 使 Frame 通用执行器无需反向依赖应用层程序集（G-3 单向依赖）。
/// </summary>
/// <param name="Ok">是否成功。</param>
/// <param name="Error">失败归因（Ok=true 时为空）。</param>
/// <param name="ErrorStep">失败步骤（诊断可见化用）。</param>
/// <param name="ErrorClass">失败类别（诊断可见化用）。</param>
public readonly record struct DeclaredFlowRunResult(bool Ok, string? Error, string? ErrorStep, string? ErrorClass)
{
    /// <summary>成功结论。</summary>
    public static DeclaredFlowRunResult Committed() => new(true, null, null, null);

    /// <summary>失败结论（归因逐字携带，供诊断可见化）。</summary>
    public static DeclaredFlowRunResult Failed(string? error, string? errorStep, string? errorClass)
        => new(false, error, errorStep, errorClass);
}
