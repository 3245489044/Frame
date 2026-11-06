using System.Collections.Generic;
using System.Text.Json.Nodes;
using FrameworkAbstractions.Serialization;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>发射层 JSON 工具（单一形态 = <see cref="JsonObject"/>；保插入序）。</summary>
public static class UsharpJson
{
    /// <summary>字符串数组（声明序；构造单源 = <see cref="JsonCollections"/>）。</summary>
    public static JsonArray Array(IEnumerable<string> values) => JsonCollections.Array(values);

    /// <summary>字符串映射（声明序；构造单源 = <see cref="JsonCollections"/>）。</summary>
    public static JsonObject Map(IEnumerable<KeyValuePair<string, string>> values) => JsonCollections.Map(values);

    /// <summary>取最后一段（单源 = <see cref="UsharpLineReader.Tail"/>）。</summary>
    public static string Tail(string value) => UsharpLineReader.Tail(value);
}
