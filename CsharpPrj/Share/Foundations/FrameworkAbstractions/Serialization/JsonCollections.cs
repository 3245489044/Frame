using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace FrameworkAbstractions.Serialization;

/// <summary>
/// JSON 集合构造（通用件）：字符串序列 → <see cref="JsonArray"/>，键值对序列 → <see cref="JsonObject"/>。
/// <para>跨层重复的「循环 Add / 循环赋值」样板收敛于此，调用方不再各自复制。</para>
/// </summary>
public static class JsonCollections
{
    /// <summary>字符串序列 → JSON 数组（保持输入序）。</summary>
    /// <param name="values">值序列。</param>
    public static JsonArray Array(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    /// <summary>键值对序列 → JSON 对象（保持输入序；重复键按后写覆盖）。</summary>
    /// <param name="values">键值对序列。</param>
    public static JsonObject Map(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var obj = new JsonObject();
        foreach (var pair in values) obj[pair.Key] = pair.Value;
        return obj;
    }
}
