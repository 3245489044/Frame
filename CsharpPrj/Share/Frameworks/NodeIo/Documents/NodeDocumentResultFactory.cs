using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
using Modules.Node.Read;

namespace Share.NodeIo;

/// <summary>
/// 写/读结果构造**单源**（D3/D4）：<c>Rejected</c> / <c>Unavailable</c> 只在此实现，
/// <see cref="NodeDocumentPort"/> 与 <see cref="NodeDocumentRegistry"/> 共用（诊断 code/severity 禁各写一套）。
/// </summary>
public static class NodeDocumentResultFactory
{
    /// <summary>写拒绝：诊断 code 恒 <c>document.rejected</c> / severity Error；不落库。</summary>
    public static NodeMaterializationResult Rejected(NodeDocumentScope scope, IEnumerable<string> reasons)
    {
        var diagnostics = reasons
            .Select(reason => new NodeDiagnostic(
                "document.rejected", NodeDiagnosticSeverity.Error, reason, scope.Domain))
            .ToArray();
        return new NodeMaterializationResult(
            MaterializeStatus.Rejected,
            scope.KeyContext,
            Array.Empty<NodePlacementResult>(),
            diagnostics);
    }

    /// <summary>
    /// 读不可用：显式三态 + 类型化原因（E10）。未显式给出 reasonCodes ⇒ 由文本原因映射
    /// （映射单源 = <see cref="NodeReadReasons.TryParse"/>；无可映射项才回落 <see cref="NodeReadReason.PlacementUnresolved"/>）。
    /// </summary>
    public static NodeReadResult Unavailable(
        string domain,
        IReadOnlyList<string> reasons,
        IReadOnlyList<NodeReadReason>? reasonCodes = null)
        => new(
            domain,
            NodeKeySetResult.Empty(NodeKeySetOrigin.Container, reasons.ToArray()),
            Array.Empty<Modules.Node.Projection.NodeProjectionItem>(),
            reasons,
            NodeReadStatus.Unavailable,
            reasonCodes ?? MapReasons(reasons));

    private static IReadOnlyList<NodeReadReason> MapReasons(IReadOnlyList<string> reasons)
    {
        var mapped = new List<NodeReadReason>();
        var seen = new HashSet<NodeReadReason>();
        foreach (var text in reasons)
        {
            if (!NodeReadReasons.TryParse(text, out var reason))
            {
                continue;
            }

            if (seen.Add(reason))
            {
                mapped.Add(reason);
            }
        }

        return mapped.Count > 0 ? mapped : new[] { NodeReadReason.PlacementUnresolved };
    }
}
