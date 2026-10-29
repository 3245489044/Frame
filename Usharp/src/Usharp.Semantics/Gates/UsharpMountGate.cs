using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：挂载绑定声明面（<c>[Mount]</c> M1–M6）。
/// <para>M1 重复 / M2 relation 必须已声明 / M3 trigger 词表 / M4 字段闭集与禁用字段 / M5 order /
/// M6 ensure 关系必须被绑定（仅当本编译存在 Mount 声明时，避免单文件关系编译误红）。</para>
/// </summary>
internal sealed class MountGateRule : IGateRule<UsharpGateModel>
{
    private static readonly HashSet<string> BuiltinTriggers = new(StringComparer.Ordinal)
    {
        "node.persisted", "round.settled", "task.created", "command.explicit", "rebuild.requested",
    };

    private static readonly HashSet<string> Stages = new(StringComparer.Ordinal)
    {
        "live", "provision", "settle", "explicit", "rebuild",
    };

    private static readonly HashSet<string> Families = new(StringComparer.Ordinal)
    {
        "structural", "presentation",
    };

    private static readonly HashSet<string> Coalesce = new(StringComparer.Ordinal)
    {
        "edge", "none",
    };

    private static readonly HashSet<string> Postures = new(StringComparer.Ordinal)
    {
        "require", "best-effort", "reduced",
    };

    public string Name => "usharp.mount";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var mounts = model.Mounts;
        if (mounts.Count == 0) return;

        var relationIds = new HashSet<string>(model.Relations.Select(r => r.OperationId), StringComparer.Ordinal);
        var obsEvents = new HashSet<string>(
            model.Declarations.OfKind(DslSymbolKind.Obs).Select(d => d.Name), StringComparer.Ordinal);
        var seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var boundRelations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mount in mounts)
        {
            ReportUnparsed(sink, mount);

            if (seen.TryGetValue(mount.MountId, out var origin))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountDuplicate,
                    $"[Mount] '{mount.MountId}' 重复声明（首次于 {origin.File}:{origin.Line}）",
                    mount.SourceFile, mount.LineNumber));
            else
                seen[mount.MountId] = (mount.SourceFile, mount.LineNumber);

            if (!relationIds.Contains(mount.Relation))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountRelationUndeclared,
                    $"[Mount] '{mount.MountId}'：relation='{mount.Relation}' 未在 [Relation] 声明",
                    mount.SourceFile, mount.LineNumber));
            }
            else
            {
                boundRelations.Add(mount.Relation);
            }

            if (mount.ForbiddenFields.Count > 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountFieldInvalid,
                    $"[Mount] '{mount.MountId}'：禁止重复关系声明字段 {string.Join(',', mount.ForbiddenFields)}（edge/src/tgt/container/scope 归 [Relation]）",
                    mount.SourceFile, mount.LineNumber));
            }

            if (mount.Triggers.Count == 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountTriggerUnknown,
                    $"[Mount] '{mount.MountId}'：trigger= 不能为空",
                    mount.SourceFile, mount.LineNumber));
            }
            foreach (var trigger in mount.Triggers)
            {
                if (BuiltinTriggers.Contains(trigger) || obsEvents.Contains(trigger)) continue;
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountTriggerUnknown,
                    $"[Mount] '{mount.MountId}'：trigger='{trigger}' 未在 [Obs] kind=event 声明，且不在内置触发集",
                    mount.SourceFile, mount.LineNumber));
            }

            if (!Stages.Contains(mount.Stage))
                InvalidField(sink, mount, "stage", mount.Stage, "live|provision|settle|explicit|rebuild");
            if (!Families.Contains(mount.Family))
                InvalidField(sink, mount, "family", mount.Family, "structural|presentation");
            if (!Coalesce.Contains(mount.Coalesce))
                InvalidField(sink, mount, "coalesce", mount.Coalesce, "edge|none");
            if (!Postures.Contains(mount.Posture))
                InvalidField(sink, mount, "posture", mount.Posture, "require|best-effort|reduced");
            if (mount.Order <= 0)
                InvalidField(sink, mount, "order", mount.Order.ToString(), ">0");
        }

        // M6：ensure 关系必须被至少一个 Mount 绑定（存在 Mount 面时才判，防单文件关系编译误红）。
        foreach (var relation in model.Relations)
        {
            if (!string.Equals(relation.Intent, UsharpLanguage.RelationIntentEnsure, StringComparison.Ordinal))
                continue;
            if (boundRelations.Contains(relation.OperationId)) continue;

            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountRelationUnbound,
                $"ensure 关系 '{relation.OperationId}' 缺少 [Mount] 绑定（归属写入口未声明触发/阶段）",
                relation.SourceFile, relation.LineNumber));
        }
    }

    private static void InvalidField(
        IConversionDiagnosticSink sink, UsharpMountDecl mount, string field, string value, string expected)
        => sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountFieldInvalid,
            $"[Mount] '{mount.MountId}'：{field}='{value}' 非法（期望 {expected}）",
            mount.SourceFile, mount.LineNumber));

    private static void ReportUnparsed(IConversionDiagnosticSink sink, UsharpMountDecl mount)
    {
        if (mount.UnparsedLines.Count == 0) return;
        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MountFieldInvalid,
            $"[Mount] '{mount.MountId}'：存在未解析字段行 {string.Join(" / ", mount.UnparsedLines)}",
            mount.SourceFile, mount.LineNumber));
    }
}
