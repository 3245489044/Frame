using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 归属边准入的可见化出口（W1）：**本层不引上层诊断类型**（<c>Modules.Node.Diagnostics</c> 不在
/// DataStoreDriven 引用面内）⇒ 以中立回调承载，由装配侧投影（如接到 <c>INodeDiagnosticsSink</c> / 日志）。
/// </summary>
public interface IReferenceEdgeDiagnostics
{
    /// <summary>边类型未在声明面登记（**拒写已发生**，本回调仅作可见化）。</summary>
    void EdgeUndeclared(string edgeType, string subject);

    /// <summary>声明面为空 ⇒ 全部边类型拒写（W-4「缺失即抛/可见化」，禁静默放行）。</summary>
    void CatalogEmpty(string sourceSubject);

    /// <summary>相邻节点端点键与当前工作流前缀不符（跨工作流脏端点）：节点键契约下非前缀端点不得原文透出。</summary>
    void NodeKeyOutOfScope(string endpointKey, string workflowId);
}

/// <summary>未装配实现：静默（拒绝仍生效，但不可见 ⇒ 拒绝模式装配时应同时给出口）。</summary>
public sealed class NullReferenceEdgeDiagnostics : IReferenceEdgeDiagnostics
{
    public static readonly NullReferenceEdgeDiagnostics Instance = new();

    private NullReferenceEdgeDiagnostics()
    {
    }

    /// <inheritdoc />
    public void EdgeUndeclared(string edgeType, string subject)
    {
    }

    /// <inheritdoc />
    public void CatalogEmpty(string sourceSubject)
    {
    }

    /// <inheritdoc />
    public void NodeKeyOutOfScope(string endpointKey, string workflowId)
    {
    }
}

/// <summary>
/// 归属边类型白名单端口（W1）：边类型**事实**的唯一声明面 = <c>Config\nodes\hierarchy-refs.ushar</c>；
/// 本端口只承载其投影（写边 owner <c>ReferenceGrounding</c> 按此准入，禁硬编码边类型表 —— 双真源 P22）。
/// </summary>
public interface IReferenceEdgeCatalog
{
    /// <summary>声明面登记的边类型集（诊断/对账用；未装配实现为空集）。</summary>
    IReadOnlyList<string> DeclaredEdgeTypes { get; }

    /// <summary>该边类型是否已在声明面登记（未登记 ⇒ 拒写 + 可见化，禁静默建边）。</summary>
    bool IsDeclared(string edgeType);
}

/// <summary>
/// 未装配实现：**报告模式**（不拒绝任何边；行为与「无白名单」现状逐字一致）—— 迁移期回退口径。
/// 切「拒绝模式」= 装配 <see cref="DeclaredReferenceEdgeCatalog"/>。
/// </summary>
public sealed class PermissiveReferenceEdgeCatalog : IReferenceEdgeCatalog
{
    public static readonly PermissiveReferenceEdgeCatalog Instance = new();

    private PermissiveReferenceEdgeCatalog()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DeclaredEdgeTypes => Array.Empty<string>();

    /// <inheritdoc />
    public bool IsDeclared(string edgeType) => true;
}

/// <summary>
/// 声明面投影实现（**拒绝模式**）：白名单 = 声明边类型集合（不在集合 ⇒ <c>false</c> ⇒ 调用方拒写 + 可见化）。
/// 声明面缺失（空集）⇒ 全部拒绝（W-4「缺失即抛/可见化」，禁回退为「全部放行」）。
/// </summary>
public sealed class DeclaredReferenceEdgeCatalog : IReferenceEdgeCatalog
{
    /// <summary>诊断主体标识（声明面文件定位：失败归因可辨）。</summary>
    public const string SourceSubject = "Config\\nodes\\hierarchy-refs.ushar";

    private static readonly StringComparer Comparer = StringComparer.Ordinal;
    private readonly HashSet<string> _declared;

    /// <param name="declaredEdgeTypes">声明面产出的边类型集合（<c>UsharpRefEdgeEmitter</c> 的 <c>edgeTypes</c>）。</param>
    /// <param name="diagnostics">可见化出口（声明面为空 ⇒ 报警，不静默放行）。</param>
    public DeclaredReferenceEdgeCatalog(
        IEnumerable<string> declaredEdgeTypes,
        IReferenceEdgeDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(declaredEdgeTypes);

        _declared = new HashSet<string>(declaredEdgeTypes.Where(t => !string.IsNullOrWhiteSpace(t)), Comparer);

        if (_declared.Count == 0)
        {
            (diagnostics ?? NullReferenceEdgeDiagnostics.Instance).CatalogEmpty(SourceSubject);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DeclaredEdgeTypes => _declared.OrderBy(t => t, Comparer).ToArray();

    /// <inheritdoc />
    public bool IsDeclared(string edgeType) =>
        !string.IsNullOrWhiteSpace(edgeType) && _declared.Contains(edgeType);
}
