using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ①：声明 id 唯一性（E-3）—— <c>[Matrix]</c> / <c>[Flow]</c> / <c>[KeySpace]</c> 同名重复。
/// <para>
/// 归因：报在**重复处自身**（不是首次声明处）；产物 winner 由 <see cref="UsharpDeclarationIdIndex{T}"/>
/// 统一为 FirstWins，禁止 Facet/Gate/Emit 三处各写一套 First/Last。
/// </para>
/// </summary>
internal sealed class DeclarationIdentityGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.declaration-identity";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        ReportDuplicates(model.Matrices, m => m.MatrixId, m => $"[Matrix] {m.MatrixId}",
            UsharpCodes.MatrixIdDuplicate, m => UsharpSourceRef.At(m.SourceFile, m.LineNumber), sink);

        ReportDuplicates(model.Flows, f => f.FlowId, f => $"[Flow] {f.FlowId}",
            UsharpCodes.FlowIdDuplicate, f => UsharpSourceRef.At(f.SourceFile, f.LineNumber), sink);

        ReportDuplicates(model.KeySpaces, k => k.Id, k => $"[KeySpace] {k.Id}",
            UsharpCodes.KeySpaceDuplicate, k => UsharpSourceRef.At(k.SourceFile, k.LineNumber), sink);
    }

    private static void ReportDuplicates<T>(
        IEnumerable<T> items,
        Func<T, string?> idOf,
        Func<T, string> labelOf,
        string code,
        Func<T, UsharpSourceRef> whereOf,
        IConversionDiagnosticSink sink)
        where T : class
    {
        var index = UsharpDeclarationIdIndex<T>.Build(items, idOf, whereOf);
        foreach (var group in index.Duplicates)
        {
            var origin = group.Occurrences[0].At;
            foreach (var occurrence in group.Occurrences.Skip(1))
            {
                sink.Report(UsharpGateDiagnostics.Error(code,
                    $"{labelOf(occurrence.Item)}: id '{group.Id}' 重复声明（首次声明于 {origin.File}:{origin.Line}）",
                    occurrence.At.File, occurrence.At.Line));
            }
        }
    }
}
