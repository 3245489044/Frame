using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ㉔：键身份语义层（<c>[Entity]</c>/<c>[KeyLexicon]</c>/<c>[AnswerShape]</c>/<c>[WindowShape]</c>/
/// <c>[KeyPolicy]</c>/<c>[KeyBridge]</c>）的结构闭包。
/// <para>判据载体 = 声明面结构事实（字段存在性 / 词表交集 / 权威面计数 / 模板前缀），不读执行者叙述。</para>
/// </summary>
internal sealed class KeyIdentityGateRule : IGateRule<UsharpGateModel>
{
    private static readonly string[] EntityRequired = { "scope", "container", "cardinality", "role", "authority" };
    private static readonly string[] AnswerShapeRequired = { "container", "round", "text", "terminal", "default" };
    private static readonly string[] WindowShapeRequired = { "parent", "member", "terminal", "default" };

    public string Name => "usharp.identity";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var entityIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in model.Entities)
        {
            if (!entityIds.Add(entity.Id))
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityEntityInvalid,
                    $"[Entity] {entity.Id}: 重复声明", entity.SourceFile, entity.LineNumber));

            foreach (var field in EntityRequired)
                if (!Has(entity, field))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityEntityInvalid,
                        $"[Entity] {entity.Id}: 缺 {field}=（语义轴字段必填）", entity.SourceFile, entity.LineNumber));
        }

        CheckLexicons(model, sink);

        var authorityFamilies = new List<UsharpKeyFamilyDecl>();
        var lexiconNames = new HashSet<string>(model.KeyLexicons.Select(l => l.Name), StringComparer.Ordinal);
        foreach (var space in model.KeySpaces)
            foreach (var family in space.Families)
            {
                if (family.KindLexicon is { Length: > 0 } lexiconRef && !lexiconNames.Contains(lexiconRef))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityShapeInvalid,
                        $"[KeySpace] {space.Id}: 族 '{family.Id}' 的 kind-lexicon='{lexiconRef}' 未声明",
                        space.SourceFile, space.LineNumber));

                if (family.Authority == true) authorityFamilies.Add(family);
            }

        foreach (var shape in model.KeyShapes)
        {
            var required = string.Equals(shape.Kind, "Answer", StringComparison.Ordinal)
                ? AnswerShapeRequired
                : WindowShapeRequired;

            foreach (var field in required)
                if (!shape.Has(field))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityShapeInvalid,
                        $"[{shape.Kind}Shape] {shape.Id}: 缺 {field}=（形状契约必填）", shape.SourceFile, shape.LineNumber));
        }

        CheckPolicies(model, sink, entityIds);
        CheckBridges(model, sink);
        CheckAuthority(model, sink, authorityFamilies);
    }

    private static void CheckLexicons(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var lexicon in model.KeyLexicons)
        {
            if (!names.Add(lexicon.Name))
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityShapeInvalid,
                    $"[KeyLexicon] {lexicon.Name}: 重复声明", lexicon.SourceFile, lexicon.LineNumber));

            if (lexicon.Values.Count == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityShapeInvalid,
                    $"[KeyLexicon] {lexicon.Name}: 值域为空（词表 = 合法值全集，禁空表）", lexicon.SourceFile, lexicon.LineNumber));

            sets[lexicon.Name] = new HashSet<string>(lexicon.Values, StringComparer.Ordinal);
        }

        // A4/J4：内容词表与过程词表、答部件与过程词表互斥（过程域不得混入内容类别/答部件）。
        CheckOverlap("ProcessKind", "ContentKind");
        CheckOverlap("AnswerPart", "ProcessKind");
        return;

        void CheckOverlap(string left, string right)
        {
            if (!sets.TryGetValue(left, out var a) || !sets.TryGetValue(right, out var b)) return;
            foreach (var value in a.Where(b.Contains))
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityLexiconOverlap,
                    $"[KeyLexicon] 词条 '{value}' 同时属于 '{left}' 与 '{right}'（词表互斥/A4）",
                    model.KeyLexicons.First(l => l.Name == left).SourceFile,
                    model.KeyLexicons.First(l => l.Name == left).LineNumber));
        }
    }

    private static void CheckPolicies(UsharpGateModel model, IConversionDiagnosticSink sink, HashSet<string> entityIds)
    {
        foreach (var policy in model.IdentityPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.Entity) || string.IsNullOrWhiteSpace(policy.Cardinality) ||
                string.IsNullOrWhiteSpace(policy.Writer) || string.IsNullOrWhiteSpace(policy.OnUnresolvable))
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityPolicyInvalid,
                    $"[KeyPolicy] {policy.Id}: entity/cardinality/writer/on_unresolvable 均为必填（唯一写者）",
                    policy.SourceFile, policy.LineNumber));

            if (!entityIds.Contains(policy.Entity))
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityPolicyInvalid,
                    $"[KeyPolicy] {policy.Id}: entity='{policy.Entity}' 未在 [Entity] 声明（悬空引用）",
                    policy.SourceFile, policy.LineNumber));
        }
    }

    private static void CheckBridges(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var bridge in model.KeyBridges)
        {
            if (string.IsNullOrWhiteSpace(bridge.From) || string.IsNullOrWhiteSpace(bridge.To) ||
                string.IsNullOrWhiteSpace(bridge.Rule) || !bridge.Reversible)
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityBridgeInvalid,
                    $"[KeyBridge] {bridge.Id}: from/to/rule 必填且 reversible=true（不可逆映射不得充当镜像）",
                    bridge.SourceFile, bridge.LineNumber));
        }
    }

    private static void CheckAuthority(
        UsharpGateModel model, IConversionDiagnosticSink sink, List<UsharpKeyFamilyDecl> authorityFamilies)
    {
        foreach (var entity in model.Entities)
        {
            var owned = authorityFamilies
                .Where(f => entity.Family.Length > 0
                    ? string.Equals(f.Id, entity.Family, StringComparison.Ordinal)
                    : f.Id.EndsWith("." + entity.Id, StringComparison.Ordinal))
                .ToList();

            if (owned.Count == 0)
            {
                sink.Report(UsharpGateDiagnostics.Info(UsharpDiagnosticCatalog.IdentityAuthorityPending,
                    $"[Entity] {entity.Id}: 尚无 authority=true 的 family（语义层已声明；语法族待接）",
                    entity.SourceFile, entity.LineNumber));
                continue;
            }

            if (owned.Count > 1)
                sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityAuthorityNotSingle,
                    $"[Entity] {entity.Id}: authority=true 的 family 有 {owned.Count} 个（{string.Join(", ", owned.Select(f => f.Id))}；须恰一个）",
                    entity.SourceFile, entity.LineNumber));

            foreach (var family in owned)
            {
                if (string.IsNullOrWhiteSpace(family.FamilyPrefix))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityAuthorityNotSingle,
                        $"族 '{family.Id}': 权威面缺族前缀（禁裸键/catch-all，S4）", entity.SourceFile, entity.LineNumber));

                if (family.Surfaces.All(s => !string.Equals(s.Surface, "Storage", StringComparison.Ordinal)))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityAuthorityNotSingle,
                        $"族 '{family.Id}': 缺 Storage 面（权威面唯一写面，S1）", entity.SourceFile, entity.LineNumber));

                if (!family.Axes.Any(a => string.Equals(a.Name, "container", StringComparison.Ordinal)))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpDiagnosticCatalog.IdentityAuthorityNotSingle,
                        $"族 '{family.Id}': 缺 container 轴（容器判据不可判定，S4/A5）", entity.SourceFile, entity.LineNumber));
            }
        }
    }

    private static bool Has(UsharpEntityDecl entity, string field)
        => field switch
        {
            "scope" => !string.IsNullOrWhiteSpace(entity.Scope),
            "container" => !string.IsNullOrWhiteSpace(entity.Container),
            "cardinality" => !string.IsNullOrWhiteSpace(entity.Cardinality),
            "role" => !string.IsNullOrWhiteSpace(entity.Role),
            "authority" => !string.IsNullOrWhiteSpace(entity.Authority),
            _ => false,
        };
}
