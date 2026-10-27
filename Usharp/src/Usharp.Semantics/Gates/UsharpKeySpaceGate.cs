using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>键/命令闸A 诊断码（单一真源；7xxx 段）。</summary>
internal static class UsharpKeyCodes
{
    public const string KeyFamilyDuplicate = "US7101";
    public const string KeyFamilyTemplateMissing = "US7102";
    public const string KeySurfaceFamilyUnknown = "US7103";
    public const string KeyPortFamilyUnknown = "US7104";
    public const string KeyPolicyDuplicate = "US7105";
    public const string KeyLineUnparsed = "US7106";

    public const string CommandDuplicate = "US7001";
    public const string CommandChannelUnknown = "US7002";
    public const string CommandOpIncomplete = "US7003";
    public const string CommandKeyFamilyUnknown = "US7004";
    public const string CommandKeySurfaceUnknown = "US7005";
    public const string CommandKeyChainFieldUnknown = "US7006";
    public const string CommandPortUnknown = "US7007";
    public const string CommandOpRefUnknown = "US7008";
    public const string CommandDefaultUnknown = "US7009";
    public const string CommandLineUnparsed = "US7010";
}

/// <summary>闸A：<c>[KeySpace]</c> 结构闭包（族/面/轴/端口/策略/回退链）。</summary>
internal sealed class KeySpaceGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.keyspace";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var familyIds = new HashSet<string>(
            model.KeySpaces.SelectMany(k => k.Families).Select(f => f.Id), StringComparer.Ordinal);
        var seenFamilies = new HashSet<string>(StringComparer.Ordinal);
        var seenPolicies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var space in model.KeySpaces)
        {
            foreach (var line in space.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeyLineUnparsed,
                    $"[KeySpace] {space.Id}: 无法解析的续行 '{line}'", space.SourceFile, space.LineNumber));

            foreach (var family in space.Families)
            {
                if (!seenFamilies.Add(family.Id))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeyFamilyDuplicate,
                        $"[KeySpace] {space.Id}: 族 '{family.Id}' 重复声明", space.SourceFile, space.LineNumber));

                if (string.IsNullOrWhiteSpace(family.Template))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeyFamilyTemplateMissing,
                        $"[KeySpace] {space.Id}: 族 '{family.Id}' 缺 template=", space.SourceFile, space.LineNumber));

                foreach (var surface in family.Surfaces)
                    if (!string.Equals(surface.FamilyId, family.Id, StringComparison.Ordinal))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeySurfaceFamilyUnknown,
                            $"[KeySpace] {space.Id}: surface 族 '{surface.FamilyId}' 与所属族 '{family.Id}' 不一致",
                            space.SourceFile, space.LineNumber));
            }

            foreach (var port in space.Ports)
                if (port.FamilyId.Length == 0 || !familyIds.Contains(port.FamilyId))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeyPortFamilyUnknown,
                        $"[KeySpace] {space.Id}: port '{port.Name}' 指向未声明族 '{port.FamilyId}'",
                        space.SourceFile, space.LineNumber));

            foreach (var policy in space.Policies)
                if (!seenPolicies.Add(policy.Key))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpKeyCodes.KeyPolicyDuplicate,
                        $"[KeySpace] {space.Id}: policy '{policy.Key}' 重复声明", space.SourceFile, space.LineNumber));
        }
    }
}
