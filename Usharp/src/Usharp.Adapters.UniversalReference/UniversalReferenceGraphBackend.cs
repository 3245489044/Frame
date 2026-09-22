using UniversalWorkflowFramework.Core;
using Usharp.Semantics;

namespace Usharp.Adapters.UniversalReference;

/// <summary>
/// <see cref="IUsharpGraphBackend"/> 的 UniversalReference 实现（本工程 = 全库唯一引 UR 处）：
/// 环检测转发 <see cref="CircularDependencyDetector"/>，可达 / 路径 / 完整性转发 <see cref="GraphValidator{TNodeId}"/>。
/// <para>本类只做转发与类型投影，不重写图算法（单一实现）；诊断的 文件:行 归因由门面
/// <see cref="UsharpGraphService"/> 统一填写，故此处仅给判据结果。</para>
/// </summary>
public sealed class UniversalReferenceGraphBackend : IUsharpGraphBackend
{
    private readonly GraphValidator<string> _validator = new();

    public bool IsAvailable => true;

    public IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IEnumerable<string> nodes, Func<string, IEnumerable<string>> outgoing)
        => CircularDependencyDetector.Analyze(nodes, outgoing).CircularDependencies
            .Select(c => (IReadOnlyList<string>)c.ResourceChain.ToList())
            .ToList();

    public IReadOnlySet<string> Reachable(string start, Func<string, IEnumerable<string>> outgoing)
        => _validator.GetReachableNodes(start, outgoing);

    public bool HasPath(string source, string target, Func<string, IEnumerable<string>> outgoing)
        => _validator.HasPath(source, target, outgoing);

    public IReadOnlyList<UsharpDiagnostic> ValidateFlowIntegrity(
        string flowId,
        IReadOnlyList<string> nodes,
        IReadOnlyList<(string Source, string Target)> edges,
        Func<string, bool> isEntry,
        Func<string, bool> isExit)
    {
        var result = _validator.ValidateIntegrity(nodes, edges, isEntry, isExit);
        if (result.IsValid) return Array.Empty<UsharpDiagnostic>();

        return new[]
        {
            UsharpDiagnostic.Error(UsharpCodes.FlowIntegrityViolation,
                $"[Flow] {flowId}: {result.Message}", GraphPlaceholder, 0),
        };
    }

    /// <summary>归因占位（由 <see cref="UsharpGraphService"/> 覆盖为流的声明位置）。</summary>
    private const string GraphPlaceholder = "(graph)";
}
