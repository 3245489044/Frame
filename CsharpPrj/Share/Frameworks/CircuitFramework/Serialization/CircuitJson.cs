using System.Text.Json;
using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Serialization;

/// <summary>
/// 电路即数据：电路是纯声明数据结构，可序列化、可 diff、可持久化。执行是它的派生行为。
/// 使用 System.Text.Json 做默认往返（JSON 是声明面与外部工具的最小公共约定）。
/// </summary>
public static class CircuitJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>将电路序列化为 JSON 文本。</summary>
    public static string Serialize(Circuit circuit) => JsonSerializer.Serialize(circuit, Options);

    /// <summary>从 JSON 文本反序列化为电路。</summary>
    public static Circuit Deserialize(string json) =>
        JsonSerializer.Deserialize<Circuit>(json, Options)
        ?? throw new InvalidOperationException("电路 JSON 反序列化结果为 null。");
}