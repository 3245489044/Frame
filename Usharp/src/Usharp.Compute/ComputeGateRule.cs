using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Compute;

/// <summary>
/// <c>[Compute]</c> 计算包的闸A 规则（<c>usharp.compute</c>）。
/// <para>
/// 首行硬短路：闭包内没有任何 <c>[Compute]</c> 标记 ⇒ 不扫描、不分析、不产生任何诊断。
/// 这是「纯声明文件诊断逐字不变」的结构性保证（不是靠条件小心）。
/// </para>
/// </summary>
internal sealed class ComputeGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.compute";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!ComputeBoundaryScanner.HasComputeMarker(model.Documents, model.Sources)) return;

        Func<string, string, bool>? coordinateExists = null;
        if (model.TryGetFacet<SpaceRegistry>(out var spaces))
            coordinateExists = (space, key) => spaces.HasCoordinate(space, key, out _);

        foreach (var boundary in ComputeBoundaryScanner.Scan(model.Documents, model.Sources))
            sink.Report(Conversion(boundary));

        var analysis = ComputeAnalysisRunner.Run(model.Documents, coordinateExists);
        foreach (var diagnostic in analysis.Diagnostics)
            sink.Report(Conversion(diagnostic));
    }

    /// <summary>严重度从中心码表取；计算码未登记 ⇒ 回退 Error（与既有码一致）。</summary>
    private static Share.LogicComposition.Conversion.Models.ConversionDiagnostic Conversion(
        ComputeDiagnostic diagnostic)
        => UsharpDiagnosticFactory.Conversion(diagnostic.Code, diagnostic.Message,
            diagnostic.File, diagnostic.Line);
}
