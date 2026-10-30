using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// JSON 提案管线（纯函数，唯一实现）：解析（JSONC 容忍）→ 键白名单/类型归一 → 合并到当前 JSON → 校验。
/// <para>表单模式按 <see cref="ParamSpec"/> 归一；模板模式按模板顶层键与示例类型校验；合并只覆盖被建议键。</summary>
public static class JsonProposalPipeline
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // 中文不转义：提案直接写入 JSON 编辑器，必须保持可读原文
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>解析为 JSON 对象（容忍注释与尾逗号）；非对象/解析失败 → null。</summary>
    public static JsonObject? TryParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return node as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>提案 JSON → 参数键值字典：只保留声明键，值按控件形态归一（未知键丢弃）。</summary>
    public static Dictionary<string, object?> ToSpecValues(string? proposalJson, IReadOnlyList<ParamSpec> specs)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var patch = TryParseObject(proposalJson);
        if (patch is null) return result;

        foreach (var spec in specs)
        {
            if (!patch.TryGetPropertyValue(spec.Key, out var node) || node is null) continue;
            result[spec.Key] = ParamSet.Coerce(spec.Kind, ToPlainValue(node));
        }
        return result;
    }

    /// <summary>
    /// 提案补丁合并到当前 JSON：只写入模板允许的键（未知键记录错误并丢弃），类型不符尝试归一。
    /// <para>当前 JSON 不可解析时以模板骨架为底，避免把非法文本原样回写。</para>
    /// </summary>
    public static JsonMergeResult MergeInto(string? currentJson, string? patchJson, string? templateJson)
    {
        var errors = new List<string>();
        var patch = TryParseObject(patchJson);
        if (patch is null)
            return JsonMergeResult.Failed("提案不是 JSON 对象");

        var allowed = JsonSchemaText.TemplateKeys(templateJson);
        var kinds = JsonSchemaText.TemplateKinds(templateJson);

        var filtered = new JsonObject();
        foreach (var kv in patch)
        {
            if (kv.Key.StartsWith("_", StringComparison.Ordinal))
            {
                errors.Add($"忽略元数据键: {kv.Key}");
                continue;
            }
            if (allowed.Count > 0 && !Contains(allowed, kv.Key))
            {
                errors.Add($"未知键: {kv.Key}");
                continue;
            }
            if (kinds.TryGetValue(kv.Key, out var target) && target != JsonValueKind.Null)
            {
                var actual = JsonSchemaText.KindOf(kv.Value);
                var coerced = actual == target ? kv.Value?.DeepClone() : TryCoerce(target, kv.Value);
                if (coerced is null)
                {
                    errors.Add($"类型不符: {kv.Key} 需要 {target}");
                    continue;
                }
                filtered[kv.Key] = coerced;
                continue;
            }
            filtered[kv.Key] = kv.Value?.DeepClone();
        }

        if (filtered.Count == 0)
            return JsonMergeResult.Failed(errors.Count > 0 ? string.Join("；", errors) : "提案为空");

        var current = TryParseObject(currentJson);
        if (current is null)
        {
            current = TryParseObject(templateJson) ?? new JsonObject();
            errors.Add("当前 JSON 不可解析，已按模板骨架生成");
        }
        else
        {
            current = (JsonObject)current.DeepClone();
        }

        var changed = new List<string>(filtered.Count);
        foreach (var kv in filtered)
        {
            current[kv.Key] = kv.Value?.DeepClone();
            changed.Add(kv.Key);
        }

        return JsonMergeResult.Ok(current.ToJsonString(Indented), changed, errors);
    }

    /// <summary>校验 JSON 对象是否满足模板约束（顶层键白名单 + 示例类型）；返回人类可读错误（空 = 通过）。</summary>
    public static IReadOnlyList<string> ValidateAgainstTemplate(string? json, string? templateJson)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return new[] { "JSON 为空" };

        var root = TryParseObject(json);
        if (root is null) return new[] { "JSON 解析失败（不是对象）" };

        var allowed = JsonSchemaText.TemplateKeys(templateJson);
        var kinds = JsonSchemaText.TemplateKinds(templateJson);

        foreach (var kv in root)
        {
            if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
            if (allowed.Count > 0 && !Contains(allowed, kv.Key))
            {
                errors.Add($"未知键: {kv.Key}");
                continue;
            }
            if (kinds.TryGetValue(kv.Key, out var target) && !Compatible(target, kv.Value))
            {
                var coerced = TryCoerce(target, kv.Value);
                if (coerced is null) errors.Add($"类型不符: {kv.Key} 需要 {target}");
            }
        }
        return errors;
    }

    private static bool Contains(IReadOnlyList<string> list, string key)
    {
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i], key, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>类型兼容判定（Null 模板值 = 任意；Number/Bool 接受可解析的字符串）。</summary>
    private static bool Compatible(JsonValueKind target, JsonNode? node)
    {
        if (target == JsonValueKind.Null) return true;
        var actual = JsonSchemaText.KindOf(node);
        if (actual == target) return true;
        if (actual == JsonValueKind.Null) return true;
        return TryCoerce(target, node) is not null;
    }

    /// <summary>按目标类型归一节点（Number/Bool 接受字符串；String 接受数字/布尔）；不可归一 → null。</summary>
    private static JsonNode? TryCoerce(JsonValueKind target, JsonNode? node)
    {
        if (node is null) return null;
        if (node.GetValueKind() == target) return node;

        if (node is not JsonValue value) return null;

        if (target == JsonValueKind.String)
        {
            // 数字/布尔落到字符串键时按原文转换（LLM 常把 id/名称写成数值）
            switch (node.GetValueKind())
            {
                case JsonValueKind.True: return JsonValue.Create("true");
                case JsonValueKind.False: return JsonValue.Create("false");
                case JsonValueKind.Number when value.TryGetValue<double>(out var number):
                    return JsonValue.Create(number.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (!value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        switch (target)
        {
            case JsonValueKind.Number:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return JsonValue.Create(l);
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return JsonValue.Create(d);
                return null;

            case JsonValueKind.String:
                return JsonValue.Create(text);

            case JsonValueKind.True:
            case JsonValueKind.False:
                if (bool.TryParse(text, out var b)) return JsonValue.Create(b);
                return null;

            default:
                return null;
        }
    }

    /// <summary>JsonNode → 原生值（JsonElement 语义的最小投影，供 ParamSet 归一）。</summary>
    private static object? ToPlainValue(JsonNode node) => node.GetValueKind() switch
    {
        JsonValueKind.String => node.GetValue<string>(),
        JsonValueKind.Number => node is JsonValue nv && nv.TryGetValue<long>(out var l) ? l : node.GetValue<double>(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => node.ToJsonString(),
    };
}

/// <summary>合并结果：合并后的 JSON 文本、被覆盖键、非致命告警。</summary>
public sealed class JsonMergeResult
{
    public string? Json { get; init; }

    public IReadOnlyList<string> ChangedKeys { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>失败原因（<see cref="Json"/> 为 null 时非空）。</summary>
    public string? Error { get; init; }

    public bool IsSuccess => Json is not null;

    public static JsonMergeResult Failed(string error) => new() { Error = error };

    public static JsonMergeResult Ok(string json, IReadOnlyList<string> changed, IReadOnlyList<string> warnings) =>
        new() { Json = json, ChangedKeys = changed, Warnings = warnings };
}
