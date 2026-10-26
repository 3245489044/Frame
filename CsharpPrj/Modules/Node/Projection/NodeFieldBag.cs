using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Projection;

/// <summary>
/// 读项字段取用**唯一入口**（FW-1）：<c>Fields</c>（载荷展平）优先，记录级 <c>RecordMeta</c> 补缺。
/// <para>
/// 纪律：调用点禁自行 <c>Fields[key]</c> 猜字段，也禁各自拼「payload ∪ meta」——两处拼即两种取值口径。
/// </para>
/// </summary>
public static class NodeFieldBag
{
    /// <summary>合成字段袋（载荷优先；未提供 meta 时直接返回载荷字段，零分配）。</summary>
    public static IReadOnlyDictionary<string, string> Of(NodeProjectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.RecordMeta is not { Count: > 0 } meta)
        {
            return item.Fields;
        }

        var merged = new Dictionary<string, string>(item.Fields, StringComparer.Ordinal);
        foreach (var pair in meta)
        {
            if (merged.ContainsKey(pair.Key))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(pair.Value))
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// 按**字段路径**取值（单源）：先查展平字段；含 <c>.</c> 时按路径走载荷 JSON。
    /// <para>
    /// 对象叶子 ⇒ 返回其**键名**逗号串（端口集合语义，如 <c>Outputs.Ports</c> ⇒ <c>filtered_input</c>）；
    /// 数组 ⇒ 逐项文本 <c>|</c> 连接；标量 ⇒ 文本。路径不可达 ⇒ <c>null</c>（不伪造）。
    /// </para>
    /// </summary>
    public static string? ValueOf(NodeProjectionItem item, string? path)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var merged = Of(item);
        if (merged.TryGetValue(path!, out var flat) && !string.IsNullOrEmpty(flat))
        {
            return flat;
        }

        if (!path!.Contains('.', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(item.PayloadJson))
        {
            return null;
        }

        if (!NodeJson.TryParse(item.PayloadJson, out var root, out _) || root is null)
        {
            return null;
        }

        JsonNode? node = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node))
            {
                return null;
            }
        }

        return node switch
        {
            null => null,
            JsonObject map => map.Count == 0
                ? null
                : string.Join(",", map.Select(pair => pair.Key)),
            JsonArray array => string.Join(
                "|",
                array.Where(element => element is not null).Select(element => NodeJson.ValueToText(element))),
            _ => NodeJson.ValueToText(node),
        };
    }
}

