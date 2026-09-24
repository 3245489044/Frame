using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modules.Node.Materialization;

public sealed class SpecDefaultsException : InvalidOperationException
{
    public SpecDefaultsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 声明缺省值（G13 收编）：<b>只填载荷字段</b>的缺失值，缺失即补、已有即保留。
/// 与位置解析<b>物理隔离</b>（P13）：本件不得参与"写哪/键是什么"的决策——模板/位置由身份与位置声明决定，
/// 故其接口只接受/返回载荷 JSON，不接触 PlacementRegistry / IdentitySpec。
/// </summary>
public sealed class SpecDefaults
{
    private readonly JsonObject _defaults = new();

    public SpecDefaults(string? defaultsJson = null)
    {
        if (!string.IsNullOrWhiteSpace(defaultsJson))
        {
            Load(defaultsJson);
        }
    }

    public int FieldCount => _defaults.Count;

    /// <summary>装载缺省声明（深度合并进已有缺省集）。</summary>
    public void Load(string defaultsJson)
    {
        var parsed = ParseObject(defaultsJson);
        MergeInto(_defaults, parsed);
    }

    public IReadOnlyList<string> FieldNames => _defaults.Select(p => p.Key).ToArray();

    /// <summary>按缺省集补齐载荷缺失字段（已存在的字段一律保留）。</summary>
    public string Merge(string payloadJson)
    {
        if (_defaults.Count == 0)
        {
            return payloadJson;
        }

        var target = ParseObject(payloadJson);
        var merged = (JsonObject)target.DeepClone();
        FillMissing(merged, _defaults);
        return merged.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return node as JsonObject ?? throw new SpecDefaultsException("缺省声明须为 JSON 对象");
        }
        catch (JsonException ex)
        {
            throw new SpecDefaultsException($"缺省声明解析失败: {ex.Message}");
        }
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (target[pair.Key] is JsonObject nested && pair.Value is JsonObject incoming)
            {
                MergeInto(nested, incoming);
                continue;
            }

            target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static void FillMissing(JsonObject target, JsonObject defaults)
    {
        foreach (var pair in defaults)
        {
            if (!target.ContainsKey(pair.Key))
            {
                target[pair.Key] = pair.Value?.DeepClone();
                continue;
            }

            if (target[pair.Key] is JsonObject nested && pair.Value is JsonObject incoming)
            {
                FillMissing(nested, incoming);
            }
        }
    }
}
