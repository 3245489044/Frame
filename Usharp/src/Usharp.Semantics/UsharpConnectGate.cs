using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑧：链间连接闸A（<c>connects=</c>）—— <c>US1015</c> / <c>US1016</c> / <c>US1017</c>。
/// <para>
/// 分工：连接**算法**（链化 / 图连接 / 执行编排）是 <c>Operation\WorkFlowMethod</c> 的既有真源，本规则只校验
/// 声明面；方向即语义（上游在前），多上游以 <c>;</c> 分段。
/// </para>
/// <para>
/// <c>US1017</c>（跨块写依赖未声明）：<c>∃c: write(B,c) ∧ read(A,c) ∧ B∉ancestors(A) ∧ ¬connects(A,B)</c>
/// —— 写集 = 各层矩阵行的输出坐标、读集 = 矩阵行内引用坐标（读写集只此一处实现，见
/// <see cref="UsharpGateModel.WrittenCoordinates"/>；与 <c>US1004</c> 的归因分工见 <c>MatrixGateRule</c>）。
/// 读域本身仍 = 自身 ∪ 祖先闭包（设计方案 §10.4 实施修正 ④），<c>connects=</c> 只表达**依赖可见性**、不放宽读域。
/// </para>
/// </summary>
internal sealed class ConnectGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.connect";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var flow in model.Flows)
            foreach (var connects in flow.Connects)
            {
                // ① 形状：边类型不可为空（复用 <上游流号>:<边类型> 的既有书写形状）。
                if (connects.EdgeType.Length == 0)
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ConnectEdgeTypeEmpty,
                        $"[Flow] {flow.FlowId}: connects 边类型为空（须形如 <上游流号>:<连接边类型>，多上游以 ';' 分段）",
                        flow.SourceFile, flow.LineNumber));
                    continue;
                }

                // ② 端点：必须在本编译内可解析，且不得自连（自连 = 环，属非法连接）。
                if (string.Equals(connects.ParentSpaceId, flow.FlowId, StringComparison.Ordinal))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ConnectEndpointUnknown,
                        $"[Flow] {flow.FlowId}: connects 指向自身（自连成环；上游须是另一流号）",
                        flow.SourceFile, flow.LineNumber));
                else if (!model.FlowIds.Contains(connects.ParentSpaceId))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ConnectEndpointUnknown,
                        $"[Flow] {flow.FlowId}: connects 上游 '{connects.ParentSpaceId}' 在本编译内不可解析"
                        + $"（已声明流：{string.Join(", ", model.FlowIds.OrderBy(id => id, StringComparer.Ordinal))}）",
                        flow.SourceFile, flow.LineNumber));
            }

        foreach (var matrix in model.Matrices)
        {
            if (string.IsNullOrWhiteSpace(matrix.OverSpaceId)) continue;

            var planFlow = model.Flows.FirstOrDefault(f => string.Equals(f.OverSpaceId, matrix.OverSpaceId, StringComparison.Ordinal));
            foreach (var row in matrix.Rows)
                foreach (var input in row.InputRefs)
                {
                    var writers = model.NonAncestorWriters(matrix.OverSpaceId, input);
                    if (writers.Count == 0) continue;

                    // 已声明的 connects= 覆盖全部非祖先写者层 ⇒ 依赖显式可见，不报。
                    if (model.UpstreamConnectedLayers.TryGetValue(matrix.OverSpaceId, out var upstream)
                        && writers.All(upstream.Contains)) continue;

                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CrossBlockWriteDependencyMissing,
                        $"[Matrix] {matrix.MatrixId}: 行 '{row.OutputKey}' 读了由非祖先块写的坐标 '{input}'"
                        + $"（写者层：{string.Join("/", writers)}）但未声明 connects=<上游流号>:<边类型>"
                        + (planFlow is null
                            ? "（本层无 [Flow] 声明，连接无处声明）"
                            : $"（声明面 = [Flow] {planFlow.FlowId} 的 connects=）"),
                        matrix.SourceFile, matrix.LineNumber));
                }
        }
    }
}
