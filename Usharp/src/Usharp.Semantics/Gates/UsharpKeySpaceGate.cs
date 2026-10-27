using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>闸A：<c>[KeySpace]</c> 结构闭包（族/面/轴/端口/策略/回退链）。</summary>
internal sealed class KeySpaceGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.keyspace";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var familyIds = new HashSet<string>(
            model.KeySpaces.SelectMany(k => k.Families).Select(f => f.Id), StringComparer.Ordinal);
        var seenFamilies = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var seenPolicies = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);

        foreach (var space in model.KeySpaces)
        {
            foreach (var line in space.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                    $"[KeySpace] {space.Id}: 无法解析的续行 '{line}'", space.SourceFile, space.LineNumber));

            foreach (var family in space.Families)
            {
                if (seenFamilies.TryGetValue(family.Id, out var origin))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyFamilyDuplicate,
                        $"[KeySpace] {space.Id}: 族 '{family.Id}' 重复声明（首次声明于 {origin.File}:{origin.Line}）",
                        origin.File, origin.Line));
                else
                    seenFamilies[family.Id] = (space.SourceFile, space.LineNumber);

                if (string.IsNullOrWhiteSpace(family.Template))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyFamilyTemplateMissing,
                        $"[KeySpace] {space.Id}: 族 '{family.Id}' 缺 template=", space.SourceFile, space.LineNumber));

                foreach (var surface in family.Surfaces)
                    if (!string.Equals(surface.FamilyId, family.Id, StringComparison.Ordinal))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeySurfaceFamilyUnknown,
                            $"[KeySpace] {space.Id}: surface 族 '{surface.FamilyId}' 与所属族 '{family.Id}' 不一致",
                            space.SourceFile, space.LineNumber));
            }

            foreach (var port in space.Ports)
                if (port.FamilyId.Length == 0 || !familyIds.Contains(port.FamilyId))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyPortFamilyUnknown,
                        $"[KeySpace] {space.Id}: port '{port.Name}' 指向未声明族 '{port.FamilyId}'",
                        space.SourceFile, space.LineNumber));

            foreach (var policy in space.Policies)
                if (seenPolicies.TryGetValue(policy.Key, out var origin))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyPolicyDuplicate,
                        $"[KeySpace] {space.Id}: policy '{policy.Key}' 重复声明（首次声明于 {origin.File}:{origin.Line}）",
                        origin.File, origin.Line));
                else
                    seenPolicies[policy.Key] = (space.SourceFile, space.LineNumber);
        }
    }
}
