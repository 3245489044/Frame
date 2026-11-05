using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 词典 / 文法分类器：字符串 → 键引用（族归属 + 结构路径）。唯一词法来源 = Modules.Parsing.Keys。
/// </summary>
public sealed class GrammarKeyClassifier : IKeyClassifier
{
    private readonly KeyGrammarRegistry _registry;
    private readonly IKeyDiagnosticsSink _diagnostics;

    public GrammarKeyClassifier(IKeySpaceDeclaration declaration, IKeyDiagnosticsSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _registry = KeyFamilyRules.BuildRegistry(declaration);
        _diagnostics = diagnostics ?? NullKeyDiagnosticsSink.Instance;
    }

    public KeyClassification Classify(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            var diagnostic = KeyDiagnostic.Info(KeyReasonCodes.Blank, KeyResolution.Empty, null, "空白键");
            _diagnostics.Report(diagnostic);
            return new KeyClassification(KeyResolution.Empty, null, diagnostic);
        }

        if (!_registry.TryResolve(raw, out var grammar))
        {
            var diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.FamilyUnresolved, KeyResolution.Unparsable, null,
                $"键未命中任何已声明族前缀: {raw}");
            _diagnostics.Report(diagnostic);
            return new KeyClassification(KeyResolution.Unparsable, null, diagnostic);
        }

        if (!grammar.TryParseAny(raw, out var values))
        {
            var unresolved = KeyRef.Of(grammar.GrammarId, raw);
            var diagnostic = KeyDiagnostic.Warn(
                KeyReasonCodes.Unparsable, KeyResolution.Unparsable, unresolved,
                $"键命中族 '{grammar.GrammarId}' 但不合模板");
            _diagnostics.Report(diagnostic);
            return new KeyClassification(KeyResolution.Unparsable, unresolved, diagnostic);
        }

        var path = new KeyPath(values.Present().Select(static pair => $"{pair.Key}={pair.Value}"));
        return new KeyClassification(
            KeyResolution.Resolved,
            new KeyRef(KeyFamilyRules.FamilyOfGrammar(grammar.GrammarId), raw, path),
            null);
    }
}
