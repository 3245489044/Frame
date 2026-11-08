using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：<c>[Segment]</c> 字段面闭合（<c>field=</c> 唯一 / <c>type=</c> 受控词 / 缺 <c>field=</c> 可见）。
/// <para>判据载体 = 声明面结构事实（字段名与类型字面量），换人可从 <c>prompt-segments.ushar</c> 直接复算。</para>
/// </summary>
internal sealed class SegmentFieldGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.segment.field";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (!model.TryGetFacet<IReadOnlyList<UsharpSegmentDecl>>(out var segments)) return;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Field))
            {
                UsharpGateDiagnostics.Report(sink, UsharpCodes.SegmentFieldMissing,
                    $"[Segment] {segment.Key}: 缺 field=（该段不进 DTO 字段面）",
                    segment.SourceFile, segment.LineNumber);
                continue;
            }

            if (!fields.TryAdd(segment.Field!, segment.Key))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SegmentFieldDuplicate,
                    $"[Segment] {segment.Key}: field='{segment.Field}' 已被段 '{fields[segment.Field!]}' 声明（字段名须唯一）",
                    segment.SourceFile, segment.LineNumber));
            }

            if (string.IsNullOrWhiteSpace(segment.Type))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SegmentTypeUnknown,
                    $"[Segment] {segment.Key}: 声明 field= 但缺 type=（词表 {string.Join('/', UsharpLanguage.SegmentTypes)}）",
                    segment.SourceFile, segment.LineNumber));
                continue;
            }

            if (Array.IndexOf(UsharpLanguage.SegmentTypes, segment.Type) < 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SegmentTypeUnknown,
                    $"[Segment] {segment.Key}: type='{segment.Type}' 不在词表（{string.Join('/', UsharpLanguage.SegmentTypes)}）",
                    segment.SourceFile, segment.LineNumber));
            }
        }
    }
}
