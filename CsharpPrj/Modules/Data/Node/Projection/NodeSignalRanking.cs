namespace Modules.Node.Projection;

/// <summary>信号排序字段值形态（决定比较方式）。</summary>
public enum NodeSignalValueKind
{
    /// <summary>布尔（<c>true/1</c> 为真）。</summary>
    Boolean,

    /// <summary>数值（不可解析视同缺失）。</summary>
    Number,

    /// <summary>文本（序数比较）。</summary>
    Text,
}

/// <summary>单个排序字段（字段名 + 值形态 + 方向）。</summary>
/// <param name="Field">字段名（由读取委托解析）。</param>
/// <param name="Kind">值形态。</param>
/// <param name="Descending">是否降序（缺省 true = 大者优先）。</param>
public sealed record NodeSignalRankField(
    string Field,
    NodeSignalValueKind Kind,
    bool Descending = true);

/// <summary>
/// 通用信号排序（稳定、可组合）：按声明字段逐一比较；**缺失字段恒置末尾**（不伪造 0/空串），
/// 同值保持输入序（稳定排序），不吞行。
/// <para>读取委托把任意信号形态投影为文本值，故同时适用于强类型信号与字段袋信号。</para>
/// </summary>
public static class NodeSignalRanking
{
    /// <summary>按字段声明排序（字段空集 ⇒ 原序返回）。</summary>
    /// <param name="items">待排序信号。</param>
    /// <param name="read">(信号, 字段名) → 文本值（null/空白 = 缺失）。</param>
    /// <param name="fields">排序字段声明（优先级从高到低）。</param>
    public static IReadOnlyList<T> Rank<T>(
        IEnumerable<T> items,
        Func<T, string, string?> read,
        IReadOnlyList<NodeSignalRankField>? fields)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(read);
        var list = items.ToList();
        if (fields is not { Count: > 0 } || list.Count < 2)
        {
            return list;
        }

        return list.OrderBy(static item => item, new RankComparer<T>(read, fields)).ToList();
    }

    private sealed class RankComparer<T> : IComparer<T>
    {
        private readonly Func<T, string, string?> _read;
        private readonly IReadOnlyList<NodeSignalRankField> _fields;

        public RankComparer(Func<T, string, string?> read, IReadOnlyList<NodeSignalRankField> fields)
        {
            _read = read;
            _fields = fields;
        }

        public int Compare(T? left, T? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;

            foreach (var field in _fields)
            {
                var leftValue = Normalize(_read(left!, field.Field));
                var rightValue = Normalize(_read(right!, field.Field));

                // 缺失恒置末尾（与升降序无关）。
                if (leftValue is null && rightValue is null) continue;
                if (leftValue is null) return 1;
                if (rightValue is null) return -1;

                var comparison = CompareValues(leftValue, rightValue, field.Kind);
                if (comparison != 0)
                {
                    return field.Descending ? -comparison : comparison;
                }
            }

            return 0;
        }

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;

        private static int CompareValues(string left, string right, NodeSignalValueKind kind)
        {
            switch (kind)
            {
                case NodeSignalValueKind.Boolean:
                    return CompareBool(ParseBool(left), ParseBool(right));
                case NodeSignalValueKind.Number:
                    return CompareNumber(left, right);
                default:
                    return string.CompareOrdinal(left, right);
            }
        }

        private static bool ParseBool(string value)
            => value.Trim() is "1" or "true" or "True" or "TRUE";

        private static int CompareBool(bool left, bool right)
            => left == right ? 0 : left ? 1 : -1;

        private static int CompareNumber(string left, string right)
        {
            if (double.TryParse(left, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var leftNumber)
                && double.TryParse(right, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rightNumber))
            {
                return leftNumber < rightNumber ? -1 : leftNumber > rightNumber ? 1 : 0;
            }

            return string.CompareOrdinal(left, right);
        }
    }
}
