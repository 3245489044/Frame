using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 实体判等（唯一实现）：先按反解后的结构字段比较（同名占位符 + 同值 ⇒ 同一实体），
/// 再回落到原值 Ordinal 相等。禁止 StartsWith / 前缀猜（I6）。
/// </summary>
public sealed class KeyEquivalence : IKeyEquivalence
{
    private readonly KeyGrammarRegistry _registry;

    public KeyEquivalence(IKeySpaceDeclaration declaration, IKeyGrammarDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _registry = KeyFamilyRules.BuildRegistry(declaration, diagnostics);
    }

    /// <summary>统一判等口径（大小写不敏感，与既有存储层一致）；所有键比较必须经本属性。</summary>
    public StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public bool SameEntity(KeyRef left, KeyRef right)
    {
        if (left.IsEmpty || right.IsEmpty)
        {
            return false;
        }

        if (Comparer.Equals(left.Value, right.Value))
        {
            return true;
        }

        // 结构判等：同一占位名 + 同一值（如 a:{id} 与 b:{id}）。
        if (!_registry.TryParse(left.Value, out _, out var leftValues, out _))
        {
            return false;
        }

        if (!_registry.TryParse(right.Value, out _, out var rightValues, out _))
        {
            return false;
        }

        var leftPresent = leftValues.Present();
        foreach (var pair in leftPresent)
        {
            if (rightValues.Present().TryGetValue(pair.Key, out var rightValue) &&
                Comparer.Equals(pair.Value, rightValue))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsSelf(KeyRef key, KeyEntity self)
    {
        if (self is null || key.IsEmpty)
        {
            return false;
        }

        if (Comparer.Equals(key.Value, self.Id))
        {
            return true;
        }

        foreach (var alias in self.Aliases)
        {
            if (SameEntity(key, alias))
            {
                return true;
            }
        }

        return false;
    }
}
