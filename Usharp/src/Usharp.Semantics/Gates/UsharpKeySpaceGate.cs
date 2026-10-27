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

                foreach (var prefix in family.DerivedPrefixes)
                    if (!family.DerivedPrefixReversibility.ContainsKey(prefix))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: 族 '{family.Id}' 派生前缀 '{prefix}' 缺 reversible= 声明"
                            + "（须显式声明可逆性；缺省不得猜）",
                            space.SourceFile, space.LineNumber));

                var canonicalNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var set in family.CanonicalSets)
                {
                    if (!canonicalNames.Add(set.Name))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: 族 '{family.Id}' 坐标规则集 '{set.Name}' 重复声明",
                            space.SourceFile, space.LineNumber));

                    if (!string.Equals(set.FamilyId, family.Id, StringComparison.Ordinal))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: canonical '{set.Name}' 属族 '{set.FamilyId}'"
                            + $" 与所在族 '{family.Id}' 不一致",
                            space.SourceFile, space.LineNumber));

                    if (!family.Axes.Any(axis => string.Equals(axis.Name, set.Axis, StringComparison.Ordinal)))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: canonical '{set.Name}' 引用的轴 '{set.Axis}' 未在族 '{family.Id}' 声明",
                            space.SourceFile, space.LineNumber));

                    if (set.Rules.Count == 0)
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: canonical '{set.Name}' 无规则（from=… 至少一条）",
                            space.SourceFile, space.LineNumber));

                    var froms = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var rule in set.Rules)
                    {
                        if (!froms.Add(rule.From))
                            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                                $"[KeySpace] {space.Id}: canonical '{set.Name}' 的 from='{rule.From}' 重复",
                                space.SourceFile, space.LineNumber));

                        if (!string.Equals(rule.OnMiss, "Fail", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(rule.OnMiss, "Keep", StringComparison.OrdinalIgnoreCase))
                            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                                $"[KeySpace] {space.Id}: canonical '{set.Name}' 的 on-miss='{rule.OnMiss}' 非法（Fail|Keep）",
                                space.SourceFile, space.LineNumber));

                        var knownRule = string.Equals(rule.Rule, "strip-prefix", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(rule.Rule, "identity", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(rule.Rule, "require-family", StringComparison.OrdinalIgnoreCase);
                        if (!knownRule)
                            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                                $"[KeySpace] {space.Id}: canonical '{set.Name}' 的 rule='{rule.Rule}' 未登记"
                                + "（strip-prefix|identity|require-family）",
                                space.SourceFile, space.LineNumber));

                        if (string.Equals(rule.Rule, "strip-prefix", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(rule.From, "*", StringComparison.Ordinal))
                            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                                $"[KeySpace] {space.Id}: canonical '{set.Name}' 的 strip-prefix 不得以 * 为 from",
                                space.SourceFile, space.LineNumber));
                    }

                    foreach (var prefix in family.DerivedPrefixes)
                        if (!froms.Contains(prefix))
                            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                                $"[KeySpace] {space.Id}: 派生前缀 '{prefix}' 缺 canonical 规则"
                                + $"（族 '{family.Id}' 规则集 '{set.Name}'；新增派生前缀必须同批声明归一口径）",
                                space.SourceFile, space.LineNumber));
                }

                foreach (var axis in family.Axes)
                    if (axis.Canonical is { } canonicalName && !canonicalNames.Contains(canonicalName))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.KeyLineUnparsed,
                            $"[KeySpace] {space.Id}: 轴 '{family.Id}/{axis.Name}' 的 canonical='{canonicalName}'"
                            + " 未在本族声明（悬空引用）",
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
