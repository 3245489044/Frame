using System.Collections.Generic;
using System.Text.Json.Nodes;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>发射层 JSON 工具（单一形态 = <see cref="JsonObject"/>；保插入序）。</summary>
public static class UsharpJson
{
    /// <summary>字符串数组（声明序）。</summary>
    public static JsonArray Array(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    /// <summary>字符串映射（声明序）。</summary>
    public static JsonObject Map(IEnumerable<KeyValuePair<string, string>> values)
    {
        var map = new JsonObject();
        foreach (var pair in values) map[pair.Key] = pair.Value;
        return map;
    }

    /// <summary>取最后一段（单源 = <see cref="UsharpLineReader.Tail"/>）。</summary>
    public static string Tail(string value) => UsharpLineReader.Tail(value);
}
