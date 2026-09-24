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
        MergeInto(_defaults, parsed, overwrite: true);
    }

    public IReadOnlyList<string> FieldNames => _defaults.Select(p => p.Key).ToArray();

    /// <summary>按缺省集补齐载荷缺失字段（已存在的字段一律保留）。</summary>
    public string Merge(string payloadJson)
    {
        if (_defaults.Count == 0)
        {
            return payloadJson;
        }

        // ParseObject 已产出全新树，无需再深拷贝（原实现多一次全载荷克隆）
        var merged = ParseObject(payloadJson);
        MergeInto(merged, _defaults, overwrite: false);
        return merged.ToJsonString(NodeJson.WriteOptions);
    }

    private static JsonObject ParseObject(string json)
    {
        if (!NodeJson.TryParse(json, out var node, out var error))
        {
            throw new SpecDefaultsException($"缺省声明解析失败: {error ?? "空声明"}");
        }

        return node as JsonObject ?? throw new SpecDefaultsException("缺省声明须为 JSON 对象");
    }

    /// <summary>
    /// 递归合并单源（原 <c>MergeInto</c>/<c>FillMissing</c> 两个同构拷贝收敛为一处）：
    /// <paramref name="overwrite"/>=<c>true</c> 为声明装载（同键后者胜），<c>false</c> 为载荷补齐（只填缺失键）。
    /// </summary>
    private static void MergeInto(JsonObject target, JsonObject source, bool overwrite)
    {
        foreach (var pair in source)
        {
            var hasTarget = target.TryGetPropertyValue(pair.Key, out var existing);

            if (hasTarget && existing is JsonObject nested && pair.Value is JsonObject incoming)
            {
                MergeInto(nested, incoming, overwrite);
                continue;
            }

            if (overwrite || !hasTarget)
            {
                target[pair.Key] = pair.Value?.DeepClone();
            }
        }
    }
}
