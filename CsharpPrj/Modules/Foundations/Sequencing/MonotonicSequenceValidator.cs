namespace Modules.Sequencing;

/// <summary>
/// 首个违序位置：<see cref="Index"/> 处序键小于（或等于，严格模式下）此前基准 <see cref="BaselineIndex"/> 的序键。
/// </summary>
/// <param name="Index">首个违序项下标。</param>
/// <param name="BaselineIndex">比较基准项下标（此前最大序键所在位置）。</param>
/// <param name="Key">违序项序键。</param>
/// <param name="BaselineKey">基准项序键。</param>
public readonly record struct SequenceViolation(int Index, int BaselineIndex, long Key, long BaselineKey);

/// <summary>
/// 序列单调校验器：按调用方给定的序键选择器判定单调性，并定位首个违序位置。
/// <para>纯算法，不识别业务字段；比较只使用 <see cref="Func{T,Long}"/> 的返回值。</para>
/// </summary>
public static class MonotonicSequenceValidator
{
    /// <summary>判断序列是否单调不减（允许相等）。</summary>
    public static bool IsNonDecreasing<T>(IReadOnlyList<T> items, Func<T, long> keySelector)
        => !TryFindFirstViolation(items, keySelector, strict: false, out _);

    /// <summary>判断序列是否严格单调递增（不允许相等）。</summary>
    public static bool IsStrictlyIncreasing<T>(IReadOnlyList<T> items, Func<T, long> keySelector)
        => !TryFindFirstViolation(items, keySelector, strict: true, out _);

    /// <summary>
    /// 定位首个违序位置。
    /// </summary>
    /// <param name="items">待校验序列。</param>
    /// <param name="keySelector">序键选择器（须为纯函数）。</param>
    /// <param name="strict">true：相等也算违序；false：相等合法。</param>
    /// <param name="violation">违序信息（无违序时为 default）。</param>
    /// <returns>存在违序返回 true。</returns>
    public static bool TryFindFirstViolation<T>(
        IReadOnlyList<T> items,
        Func<T, long> keySelector,
        bool strict,
        out SequenceViolation violation)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);

        violation = default;
        if (items.Count <= 1) return false;

        var baselineIndex = 0;
        var baselineKey = keySelector(items[0]);

        for (var i = 1; i < items.Count; i++)
        {
            var key = keySelector(items[i]);
            var outOfOrder = strict ? key <= baselineKey : key < baselineKey;
            if (outOfOrder)
            {
                violation = new SequenceViolation(i, baselineIndex, key, baselineKey);
                return true;
            }

            if (key > baselineKey)
            {
                baselineIndex = i;
                baselineKey = key;
            }
        }

        return false;
    }
}
