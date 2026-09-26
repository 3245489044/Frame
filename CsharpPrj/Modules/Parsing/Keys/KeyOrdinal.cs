using System.Globalization;
using System.Text.RegularExpressions;

namespace Modules.Parsing.Keys;

/// <summary>
/// 键序（**唯一比较实现**）：两族键序共用一个入口，禁各处就地 <c>OrderBy(ParseRound/ParseSeq)</c> 造第二口径。
/// <list type="bullet">
/// <item><see cref="Segmentwise"/>：点分段 + 段内「前缀 + 数字」数值序（既有 <c>NodeKeyOrdinalComparer</c> 语义）。</item>
/// <item><see cref="ForGrammar"/>：按文法声明的键序项逐项比较（数值段数值序、文本段序数序），末尾以整键序数序兜底。</item>
/// </list>
/// <para>纪律：不合形键 / 超长数字段一律退化序数比较（不抛、不猜；禁对键串直接字典序比较数值段）。</para>
/// </summary>
public static class KeyOrdinal
{
    private static readonly Regex NumericSuffix = new(@"^(?<prefix>\D*)(?<number>\d+)$", RegexOptions.Compiled);

    /// <summary>点分段数值感知比较器（读写两侧共用；枚举序 = 键序数序，禁随载体枚举序漂移）。</summary>
    public static readonly IComparer<string> Segmentwise = Comparer<string>.Create(CompareSegments);

    /// <summary>点分段数值感知比较（<c>a.2 &lt; a.10</c>；前缀不同则序数比较）。</summary>
    public static int CompareSegments(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Min(leftParts.Length, rightParts.Length);

        for (var index = 0; index < length; index++)
        {
            var comparison = CompareSegment(leftParts[index], rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    /// <summary>按文法键序项比较（<see cref="KeyGrammar.OrderKeys"/> 顺序；缺席段视为最小值 = 前置）。</summary>
    public static IComparer<string> ForGrammar(KeyGrammar grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        return new GrammarComparer(grammar);
    }

    private static int CompareSegment(string left, string right)
    {
        var leftMatch = NumericSuffix.Match(left);
        var rightMatch = NumericSuffix.Match(right);

        if (leftMatch.Success && rightMatch.Success &&
            string.Equals(leftMatch.Groups["prefix"].Value, rightMatch.Groups["prefix"].Value, StringComparison.Ordinal) &&
            long.TryParse(leftMatch.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue) &&
            long.TryParse(rightMatch.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue))
        {
            return leftValue.CompareTo(rightValue);
        }

        return string.CompareOrdinal(left, right);
    }

    private sealed class GrammarComparer : IComparer<string>
    {
        private readonly KeyGrammar _grammar;

        internal GrammarComparer(KeyGrammar grammar) => _grammar = grammar;

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            if (!_grammar.TryParseAny(left, out var leftValues, out _) ||
                !_grammar.TryParseAny(right, out var rightValues, out _))
            {
                return string.CompareOrdinal(left, right);
            }

            foreach (var key in _grammar.OrderKeys)
            {
                var comparison = _grammar.IsNumeric(key)
                    ? CompareNumeric(leftValues, rightValues, key)
                    : string.CompareOrdinal(leftValues.Text(key) ?? string.Empty,
                        rightValues.Text(key) ?? string.Empty);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return string.CompareOrdinal(left, right);
        }

        private static int CompareNumeric(KeyValues left, KeyValues right, string key)
        {
            var leftValue = left.TryGetLong(key, out var parsedLeft) ? parsedLeft : long.MinValue;
            var rightValue = right.TryGetLong(key, out var parsedRight) ? parsedRight : long.MinValue;
            return leftValue.CompareTo(rightValue);
        }
    }
}
