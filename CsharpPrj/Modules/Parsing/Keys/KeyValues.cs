using System.Globalization;

namespace Modules.Parsing.Keys;

/// <summary>
/// 键值袋（反解产物，**唯一形态**）：占位符名 → 文本值，并区分「出现」与「缺席」（可缺省区未参与匹配）。
/// 数值取用一律 <see cref="NumberStyles.None"/> + InvariantCulture（拒绝符号/空白/分组，对齐既有键解析口径）。
/// </summary>
public sealed class KeyValues
{
    /// <summary>空值袋（失败返回，调用方无需判 null）。</summary>
    public static readonly KeyValues Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _present;

    internal KeyValues(Dictionary<string, string> values, HashSet<string> present)
    {
        _values = values;
        _present = present;
    }

    /// <summary>全部占位符名（含缺席者可缺省段，序 = 文法段序）。</summary>
    public IReadOnlyCollection<string> Names => _values.Keys;

    /// <summary>该段是否实际出现（可缺省区未匹配 ⇒ false）。</summary>
    public bool Has(string name) => _present.Contains(name);

    /// <summary>取文本值；缺席 ⇒ null（不返回空串冒充「存在且为空」）。</summary>
    public string? Text(string name) => _present.Contains(name) ? _values[name] : null;

    /// <summary>取文本值（缺席 ⇒ false）。</summary>
    public bool TryGetText(string name, out string value)
    {
        if (_present.Contains(name))
        {
            value = _values[name];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>取非负整数（缺席 / 非纯数字 ⇒ false）。</summary>
    public bool TryGetLong(string name, out long value)
    {
        if (TryGetText(name, out var text) &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>取整数（缺席 / 非纯数字 / 溢出 ⇒ false）。</summary>
    public bool TryGetInt(string name, out int value)
    {
        if (TryGetText(name, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>快照（含缺席者可缺省段，值为空串；供对拍 / 序列化）。</summary>
    public IReadOnlyDictionary<string, string> ToDictionary() =>
        new Dictionary<string, string>(_values, StringComparer.Ordinal);

    /// <summary>仅实际出现的段（序 = 文法段序）。</summary>
    public IReadOnlyDictionary<string, string> Present()
        => _values.Where(pair => _present.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    internal bool IsPresent(string name) => _present.Contains(name);

    internal bool TryGetRaw(string name, out string value) => _values.TryGetValue(name, out value!);
}
