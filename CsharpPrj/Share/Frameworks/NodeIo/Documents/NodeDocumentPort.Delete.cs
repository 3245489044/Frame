using Modules.Node.Diagnostics;
using Modules.Node.Materialization;

namespace Share.NodeIo;

public sealed partial class NodeDocumentPort
{
    private Task<NodeMaterializationResult> DeleteAsync(
        NodeDocumentChange change,
        ResolvedScope resolved,
        CancellationToken cancellationToken)
    {
        // 写路径收敛：删除不再自建写门、不再直连载体；
        // 由框架 DeleteAsync 在同一写门内执行，键形门 / 运行域门 / 载体异常隔离与 Upsert 同源。
        return _framework.Materializer.DeleteAsync(
            new NodeMaterializeRequest(
                change.Scope.KeyContext,
                string.Empty,
                change.Scope.Domain,
                resolved.PlacementIds,
                Meta: change.RecordMeta),
            cancellationToken);
    }

    private NodeMaterializationResult Rejected(NodeDocumentScope scope, IReadOnlyList<string> reasons)
    {
        // 结果构造单源（D3）：诊断 code/severity 只由 NodeDocumentResultFactory 决定。
        var result = NodeDocumentResultFactory.Rejected(scope, reasons);
        Report(result.Diagnostics);
        return result;
    }

    private void Report(IReadOnlyList<NodeDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _framework.Diagnostics.Report(diagnostic);
        }
    }

}
