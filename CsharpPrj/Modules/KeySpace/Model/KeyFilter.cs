using System.Globalization;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Model;

/// <summary>
/// 键过滤（族 + 面 + 作用域坐标 + 轴约束）：只编译到既有 selection 极小文法，不自建选择语言。
/// <para>
/// 约束轴由调用方给出（框架不内置 kind/round 之类域词）；未声明约束 ⇒ <see cref="ToSelection"/> = <c>true</c>，
/// 与 <c>selection=all</c> 同义（末路完备，不留空出口）。
/// </para>
/// </summary>
public sealed record KeyFilter(
    string Family,
    KeySurface Surface,
    KeyPoint Scope,
    KeyAxis? KindAxis,
    IReadOnlyList<string> Kinds,
    KeyAxis? RoundAxis,
    IReadOnlyList<int> Rounds)
{
    /// <summary>无约束过滤（族 × 面 × 作用域）。</summary>
    public static KeyFilter For(string family, KeyPoint scope, KeySurface surface = KeySurface.Storage)
        => new(family, surface, scope, null, Array.Empty<string>(), null, Array.Empty<int>());

    /// <summary>
    /// 编译到既有 selection 文法（<c>&lt;轴&gt; in (a;b)</c>）；无约束 ⇒ <c>true</c>。
    /// <para>只输出单谓词：仓库既有 selection 表达式同为单谓词形态（多条件由声明面分组表达）。</para>
    /// </summary>
    public string ToSelection()
    {
        if (KindAxis is { IsEmpty: false } kindAxis && Kinds.Count > 0)
        {
            return $"{kindAxis.Name} in ({string.Join(';', Kinds)})";
        }

        if (RoundAxis is { IsEmpty: false } roundAxis && Rounds.Count > 0)
        {
            return $"{roundAxis.Name} in ({string.Join(';', Rounds.Select(static r => r.ToString(CultureInfo.InvariantCulture)))})";
        }

        return "true";
    }

    /// <summary>判定键是否命中本过滤（族/面/作用域轴值/约束均须相符）。</summary>
    public bool Matches(KeyOps ops, string? key, out KeyInstance parsed)
    {
        ArgumentNullException.ThrowIfNull(ops);
        parsed = default;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!ops.TryParse(Family, Surface, key, out parsed, out _)) return false;

        foreach (var pair in Scope.Values)
        {
            var actual = parsed.Value(KeyAxis.Of(pair.Key));
            if (!string.Equals(actual, pair.Value, StringComparison.Ordinal)) return false;
        }

        if (KindAxis is { IsEmpty: false } kindAxis && Kinds.Count > 0)
        {
            var actual = parsed.Value(kindAxis);
            if (actual is null || !Kinds.Contains(actual, StringComparer.Ordinal)) return false;
        }

        if (RoundAxis is { IsEmpty: false } roundAxis && Rounds.Count > 0)
        {
            var actual = parsed.Value(roundAxis);
            if (!int.TryParse(actual, NumberStyles.None, CultureInfo.InvariantCulture, out var round) ||
                !Rounds.Contains(round))
            {
                return false;
            }
        }

        return true;
    }
}
