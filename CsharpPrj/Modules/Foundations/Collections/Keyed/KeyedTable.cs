namespace Modules.Foundations.Collections.Keyed;

/// <summary>
/// 已构造键的只读查找表（通用；零领域词）。
/// <para>
/// 语义边界：**只做查找** —— 不解析键、不做归一化（大小写/trim/别名一律不碰）、不登记 KeyOps。
/// 键由调用方在构造前确定（编译期产物），本件只保证「有则返回、无则 false」。
/// </para>
/// <para>空/空白键视为未命中（不抛）：与既有表「拒空白键」的运行时行为一致。</para>
/// </summary>
/// <typeparam name="T">条目类型（引用类型）。</typeparam>
public readonly struct KeyedTable<T> where T : class
{
    private readonly IReadOnlyDictionary<string, T> _entries;

    /// <summary>以调用方已构造的键值表创建只读视图（不复制、不校验键形态）。</summary>
    /// <param name="entries">已构造键的只读字典（null ⇒ 抛）。</param>
    public KeyedTable(IReadOnlyDictionary<string, T> entries)
        => _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    /// <summary>按键取条目（空/空白键或未声明 ⇒ false，不伪造默认值）。</summary>
    /// <param name="key">已构造的键（本件不做任何归一化）。</param>
    /// <param name="value">条目（未命中为 null）。</param>
    public bool TryGet(string? key, out T? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;
            return false;
        }

        return _entries.TryGetValue(key!, out value);
    }

    /// <summary>按键取条目；未声明 ⇒ 抛（显式失败，异常文案由调用方给出）。</summary>
    /// <param name="key">已构造的键。</param>
    /// <param name="errorMessage">未命中时的异常文案（调用方保留原有文案）。</param>
    public T Require(string key, string errorMessage)
        => TryGet(key, out var value)
            ? value!
            : throw new InvalidOperationException(errorMessage);
}
