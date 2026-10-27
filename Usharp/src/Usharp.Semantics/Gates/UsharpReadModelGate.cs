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
}
