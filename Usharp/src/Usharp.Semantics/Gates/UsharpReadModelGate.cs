using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：<c>[ReadModel]</c> 结构闭包（source/version/ttl/projector 自洽 + op.model 双向闭合）。
/// <para>判据口径 = 声明处选版本源（projector 自带版本 ⇒ 无字段可写 ⇒ 天然不可表达）；死声明即红。</para>
/// </summary>
internal sealed class ReadModelGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.readmodel";

    private static readonly HashSet<string> Versions = new(StringComparer.Ordinal)
    {
        "None", "Ttl", "Event", "GraphMembership",
    };

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var readModel in model.ReadModels)
        {
            foreach (var line in readModel.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelLineUnparsed,
                    $"[ReadModel] {readModel.ModelId}: 无法解析的续行 '{line}'",
                    readModel.SourceFile, readModel.LineNumber));

            if (!declared.Add(readModel.ModelId))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelDuplicate,
                    $"[ReadModel] {readModel.ModelId}: 重复声明",
                    readModel.SourceFile, readModel.LineNumber));

            if (readModel.Sources.Count == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelSourceMissing,
                    $"[ReadModel] {readModel.ModelId}: 缺 source=（至少一个源键）",
                    readModel.SourceFile, readModel.LineNumber));

            if (!Versions.Contains(readModel.Version))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelVersionUnknown,
                    $"[ReadModel] {readModel.ModelId}: version='{readModel.Version}' 不在词表（None|Ttl|Event|GraphMembership）",
                    readModel.SourceFile, readModel.LineNumber));

            if (string.Equals(readModel.Version, "Ttl", StringComparison.Ordinal) && readModel.TtlMs is null)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelTtlInvalid,
                    $"[ReadModel] {readModel.ModelId}: version=Ttl 但 ttl= 缺失或非正",
                    readModel.SourceFile, readModel.LineNumber));

            if (string.IsNullOrWhiteSpace(readModel.Projector))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelProjectorMissing,
                    $"[ReadModel] {readModel.ModelId}: 缺 projector=",
                    readModel.SourceFile, readModel.LineNumber));

            CheckFields(readModel, model, sink);
        }

        foreach (var command in model.Commands)
            foreach (var op in command.Ops)
            {
                if (op.Model is not { } modelId) continue;
                if (declared.Contains(modelId)) referenced.Add(modelId);
                else
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelOpRefUnknown,
                        $"[Command] {command.Action}.{op.Op}: model '{modelId}' 未声明",
                        command.SourceFile, command.LineNumber));
            }

        foreach (var readModel in model.ReadModels)
            if (declared.Contains(readModel.ModelId) && !referenced.Contains(readModel.ModelId))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelUnused,
                    $"[ReadModel] {readModel.ModelId}: 死声明（无任何 op model= 引用）",
                    readModel.SourceFile, readModel.LineNumber));
    }

    /// <summary>
    /// 字段面闭合（US7711–US7713）：<c>fields=</c> 的每项须指向已声明 <c>[Segment]</c> 且该段有 <c>field=</c>。
    /// <para>未声明 <c>fields=</c> ⇒ 合法（无字段面，零回归）；声明了但解析为空 ⇒ 红（禁空字段面冒充已声明）。</para>
    /// </summary>
    private static void CheckFields(UsharpReadModelDecl readModel, UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!readModel.FieldsDeclared) return;

        if (readModel.Fields.Count == 0)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelFieldsEmpty,
                $"[ReadModel] {readModel.ModelId}: 声明了 fields= 但解析后为空（须列出 ≥1 个段键）",
                readModel.SourceFile, readModel.LineNumber));
            return;
        }

        if (!model.TryGetFacet<IReadOnlyList<UsharpSegmentDecl>>(out var segments)) return;

        var byKey = new Dictionary<string, UsharpSegmentDecl>(StringComparer.Ordinal);
        foreach (var segment in segments) byKey[segment.Key] = segment;

        foreach (var key in readModel.Fields)
        {
            if (!byKey.TryGetValue(key, out var segment))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelFieldsUndeclared,
                    $"[ReadModel] {readModel.ModelId}: fields= 引用未声明段 '{key}'",
                    readModel.SourceFile, readModel.LineNumber));
                continue;
            }

            if (string.IsNullOrWhiteSpace(segment.Field))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ReadModelFieldNoField,
                    $"[ReadModel] {readModel.ModelId}: fields= 引用段 '{key}' 缺 field=（字段面不可再生）",
                    readModel.SourceFile, readModel.LineNumber));
            }
        }
    }
}
