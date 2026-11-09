using System.Text.Json;
using System.Text.Json.Serialization;

namespace CallCenter.LlmGate.Ledger;

/// <summary>账本/读模型 JSON 契约（单源；camelCase + 枚举字符串 + 不忽略 null）。</summary>
public static class GateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
