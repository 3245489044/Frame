namespace Usharp.Semantics;

/// <summary>
/// 空图后端（默认实现）：全 no-op —— 环检测返回空（不误报）、可达仅自身、路径恒 false、流完整性不产出诊断。
/// 语义：**未装配图框架 = 纯声明校验模式**，闸A/引用即边/诊断仍完整可用；图类判据的缺失经
/// <see cref="UsharpCodes.GraphBackendUnavailable"/> 显式可见化（不静默、不假装通过）。
/// </summary>
public sealed class NullGraphBackend : IUsharpGraphBackend
{
    /// <summary>单例（无状态）。</summary>
    public static readonly NullGraphBackend Instance = new();

    private NullGraphBackend() { }

    public bool IsAvailable => false;

    public IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IEnumerable<string> nodes, Func<string, IEnumerable<string>> outgoing)
        => Array.Empty<IReadOnlyList<string>>();

    public IReadOnlySet<string> Reachable(string start, Func<string, IEnumerable<string>> outgoing)
        => new HashSet<string>(new[] { start }, StringComparer.Ordinal);

    public bool HasPath(string source, string target, Func<string, IEnumerable<string>> outgoing) => false;

    public IReadOnlyList<UsharpDiagnostic> ValidateFlowIntegrity(
        string flowId,
        IReadOnlyList<string> nodes,
        IReadOnlyList<(string Source, string Target)> edges,
        Func<string, bool> isEntry,
        Func<string, bool> isExit)
        => Array.Empty<UsharpDiagnostic>();

    public string RenderReport(IReadOnlyList<UsharpDiagnostic> diagnostics)
        => diagnostics.Count == 0
            ? "(no diagnostics)"
            : string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));
}
