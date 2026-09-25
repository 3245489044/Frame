using Modules.Node.Diagnostics;
using Modules.Node.Placement;
using Modules.Node.Projection;

namespace Modules.Node.Spec;

/// <summary>一份声明文档（链/节点/位置/指令；载荷恒为 JSON 文本）。</summary>
public sealed record SpecDocument(string SourceId, string Name, string Json);

/// <summary>
/// 定义集（与语言无关）：JSON 与声明语言都只是它的两种装配。
/// <b>不含任何语言类型</b>（P20）：桥接只在语言适配工程。
/// </summary>
public sealed record DefinitionSet(
    string SourceId,
    IReadOnlyList<SpecDocument> Chains,
    IReadOnlyList<SpecDocument> Nodes,
    IReadOnlyList<SpecDocument> Placements,
    IReadOnlyList<SpecDocument> Directives)
{
    public static DefinitionSet Empty(string sourceId) =>
        new(sourceId, Array.Empty<SpecDocument>(), Array.Empty<SpecDocument>(), Array.Empty<SpecDocument>(), Array.Empty<SpecDocument>());

    public int DocumentCount => Chains.Count + Nodes.Count + Placements.Count + Directives.Count;
}

/// <summary>声明来源端口（定义来源抽象；未装配 → ∅ + 可见化，不假装通过）。</summary>
public interface ISpecSource
{
    string SourceId { get; }

    Task<DefinitionSet> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>未装配声明来源：空集 + 诊断（对齐 US9001 可见化范式）。</summary>
public sealed class NullSpecSource : ISpecSource
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullSpecSource(INodeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    public string SourceId => "null";

    public Task<DefinitionSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        _diagnostics.Report(new NodeDiagnostic(
            "spec.source-unbound",
            NodeDiagnosticSeverity.Warning,
            "声明来源未装配：定义集为空（语言类判据跳过并可见化，不假装通过）",
            SourceId));
        return Task.FromResult(DefinitionSet.Empty(SourceId));
    }
}

/// <summary>定义期诊断端口（单向投影：语言/宿主 → 框架）。</summary>
public interface IDefinitionDiagnostics
{
    void Report(string code, NodeDiagnosticSeverity severity, string message, string? subject = null);
}

public sealed class NullDefinitionDiagnostics : IDefinitionDiagnostics
{
    public static readonly NullDefinitionDiagnostics Instance = new();

    private NullDefinitionDiagnostics()
    {
    }

    public void Report(string code, NodeDiagnosticSeverity severity, string message, string? subject = null)
    {
    }
}

/// <summary>
/// 运行态来源判定端口（N9：无来源 ⇒ ∅，不伪造）。
/// <para>
/// **#pending（B-4）**：框架公开契约，当前零消费方（读投影的 Origin 由调用方经
/// <c>NodeProjectionItem.Origin</c> 显式给出）。保留为「事件域落地时按 key 反查来源」的预留口，
/// **不删**（删除属跨层契约收缩，须与事件域方案一并裁定）；启用前不得假定其已生效。
/// </para>
/// </summary>
public interface IOrientationAuthority
{
    ProjectionOrigin? Resolve(string key, string placementId);
}

public sealed class NullOrientationAuthority : IOrientationAuthority
{
    public static readonly NullOrientationAuthority Instance = new();

    private NullOrientationAuthority()
    {
    }

    public ProjectionOrigin? Resolve(string key, string placementId) => null;
}

/// <summary>引用字段索引端口（G11：引用字段清单 + 反向引用；声明化，非硬编码）。</summary>
public interface IReferenceIndexPort
{
    IReadOnlyList<string> ReferenceFields { get; }

    IReadOnlyList<string> ReverseReferences(string key);
}

public sealed class NullReferenceIndexPort : IReferenceIndexPort
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullReferenceIndexPort(INodeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    public IReadOnlyList<string> ReferenceFields => Array.Empty<string>();

    public IReadOnlyList<string> ReverseReferences(string key)
    {
        _diagnostics.Report(new NodeDiagnostic(
            "spec.reference-index-unbound",
            NodeDiagnosticSeverity.Warning,
            "引用字段索引未装配：反向引用不可用（对账跳过并可见化）",
            key));
        return Array.Empty<string>();
    }
}

/// <summary>位置声明来源端口（位置声明/反解产物的注入点；框架不硬编码 loc→carrier）。</summary>
public interface IPlacementSource
{
    PlacementRegistry Load();
}
