using Operation.HierarchyMethod.Nodes.Workflows;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 一次 <c>[Flow]</c> 发射的产物：骨架契约（<see cref="RoundSpec"/>）+ 链转移表 + 发射期诊断。
/// <para><see cref="RoundSpec.Name"/> 取流号（配置文件名如 <c>agent-loop</c> 不参与等价比对：二者是不同命名域）。</para>
/// </summary>
public sealed record UsharpFlowEmitResult(
    RoundSpec? Spec,
    IReadOnlyDictionary<string, string>? TransitionTable,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no emit diagnostics)");
}
