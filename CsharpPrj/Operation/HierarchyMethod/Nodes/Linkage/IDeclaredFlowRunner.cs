using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 声明流执行器端口（框架）—— 「已声明的一条 [Flow] 跑一次」的抽象；
/// 具体规格解析与运行体由应用层注入（Frame 不感知应用层工作流类型）。
/// </summary>
public interface IDeclaredFlowRunner
{
    /// <summary>跑一次声明流。</summary>
    /// <param name="workflowFile">声明文件名（如 attribution.ushar）。</param>
    /// <param name="inports">入参与其值（null 值不入参，等价「未给」）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<DeclaredFlowRunResult> RunAsync(
        string workflowFile, IReadOnlyDictionary<string, object?> inports, CancellationToken ct = default);
}
