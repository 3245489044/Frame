using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// 目标 schema 文本（提示词用，唯一实现）：参数声明 → 键/形态/约束；JSON 模板 → 顶层键/示例类型/说明。
/// <para>只做人类可读摘要，不做完整 JSON Schema 推导（模板示例值仅用于类型提示）。</para>
/// </summary>
public static class JsonSchemaText
{
    /// <summary>表单能力参数声明 → schema 文本（键/类型/必填/默认/约束/候选/说明）。</summary>
    public static string FromSpecs(IEnumerable<ParamSpec> specs)
    {
        var sb = new StringBuilder();
        foreach (var spec in specs)
        {
            sb.Append("- ").Append(spec.Key)
              .Append(" (").Append(ParamSet.Label(spec)).Append("): ").Append(spec.Kind);
            if (spec.Required) sb.Append(", 必填");
            if (spec.Default is not null) sb.Append(", 默认=").Append(spec.Default);
            if (spec.Min is { } min) sb.Append(", min=").Append(min);
            if (spec.Max is { } max) sb.Append(", max=").Append(max);
            if (spec.Options is { Count: > 0 }) sb.Append(", 候选=").Append(string.Join("/", spec.Options));
            if (!string.IsNullOrWhiteSpace(spec.Help)) sb.Append(", 说明=").Append(spec.Help);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>JSON 模板 → schema 文本：`_doc` 摘要 + 顶层键（排除 `_` 前缀元数据）及其示例类型。</summary>
    public static string FromTemplate(string? templateJson)
    {
        var root = JsonProposalPipeline.TryParseObject(templateJson);
        if (root is null) return "";

        var sb = new StringBuilder();
        if (root.TryGetPropertyValue("_doc", out var doc) && doc is JsonValue dv
            && dv.TryGetValue<string>(out var docText) && !string.IsNullOrWhiteSpace(docText))
            sb.Append("说明：").Append(docText).Append('\n');

        foreach (var kv in root)
        {
            if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
            sb.Append("- ").Append(kv.Key).Append(" (").Append(KindOf(kv.Value)).Append("): ")
              .Append(Sample(kv.Value)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>模板允许键（顶层、非 `_` 前缀）；解析失败返回空集合（= 不限制键）。</summary>
    public static IReadOnlyList<string> TemplateKeys(string? templateJson)
    {
        var root = JsonProposalPipeline.TryParseObject(templateJson);
        if (root is null) return Array.Empty<string>();
        return root.Select(kv => kv.Key)
            .Where(k => !k.StartsWith("_", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>模板键集合 → 键示例类型（用于提案类型校验）。</summary>
    public static IReadOnlyDictionary<string, JsonValueKind> TemplateKinds(string? templateJson)
    {
        var root = JsonProposalPipeline.TryParseObject(templateJson);
        if (root is null) return new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);

        var map = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
        foreach (var kv in root)
        {
            if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
            map[kv.Key] = KindOf(kv.Value);
        }
        return map;
    }

    /// <summary>JSON 节点示例类型（null → Null）。</summary>
    public static JsonValueKind KindOf(JsonNode? node) => node?.GetValueKind() ?? JsonValueKind.Null;

    private static string Sample(JsonNode? node)
    {
        if (node is null) return "null";
        var raw = node.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        return raw.Length <= 80 ? raw : raw[..77] + "...";
    }
}
