using System.Globalization;
using System.Text.RegularExpressions;

namespace Modules.Node.Projection;

/// <summary>
/// 枚举序 = 键序数序（N6）：数字段按数值比较，非数字段按序号字符串比较。
/// 读写两侧共用同一实现——裸端口"先到者胜"必须按键序数序判定，禁随载体枚举序漂移（提取分册 §9 勘误③）。
/// </summary>
public sealed class NodeKeyOrdinalComparer : IComparer<string>
{
    public static readonly NodeKeyOrdinalComparer Instance = new();

    private static readonly Regex NumericSuffix = new(@"^(?<prefix>\D*)(?<number>\d+)$", RegexOptions.Compiled);

    private NodeKeyOrdinalComparer()
    {
    }

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

        // 超长数字段（long 溢出）与非数字段一律退化序数比较：不得因溢出抛异常
        return string.CompareOrdinal(left, right);
    }
}
