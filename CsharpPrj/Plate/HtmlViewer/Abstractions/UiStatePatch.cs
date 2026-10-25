#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UiBackend.Abstractions;

/// <summary>
/// 页面受控态回投补丁（B2）：把 <c>uiState</c> 一次解析为「展开键 + 选择键 + 标量槽」，
/// 供 <see cref="UiPageStateStore"/> 单点应用。解析失败 → null（调用方可见化，不静默吞）。
/// </summary>
public sealed record UiStatePatch(
    IReadOnlyList<string> Open,
    IReadOnlyList<string> Select,
    IReadOnlyDictionary<string, string?> Scalars,
    IReadOnlyDictionary<string, string?> Inputs)
{
    public static UiStatePatch Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, string?>(StringComparer.Ordinal),
        new Dictionary<string, string?>(StringComparer.Ordinal));

    public bool IsEmpty => Open.Count == 0 && Select.Count == 0 && Scalars.Count == 0 && Inputs.Count == 0;

    /// <summary>解析 uiState 原文（非对象/非法 JSON → null：调用方据此报差集/格式问题）。</summary>
    public static UiStatePatch? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>解析 uiState 对象：open/select 为键集合，其余属性为标量槽（对象/数组取原文，保留可见性）。</summary>
    public static UiStatePatch? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var open = new List<string>();
        var select = new List<string>();
        var scalars = new Dictionary<string, string?>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "open", StringComparison.OrdinalIgnoreCase))
                ReadKeys(property.Value, open);
            else if (string.Equals(property.Name, "select", StringComparison.OrdinalIgnoreCase))
                ReadKeys(property.Value, select);
            else if (string.Equals(property.Name, "input", StringComparison.OrdinalIgnoreCase))
                ReadInputs(property.Value, inputs);
            else
                scalars[property.Name] = ReadScalar(property.Value);
        }

        return new UiStatePatch(open, select, scalars, inputs);
    }

    private static void ReadKeys(JsonElement value, List<string> target)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var key = ReadScalar(item);
                if (!string.IsNullOrWhiteSpace(key)) target.Add(key!);
            }
            return;
        }

        var single = ReadScalar(value);
        if (!string.IsNullOrWhiteSpace(single)) target.Add(single!);
    }

    private static void ReadInputs(JsonElement value, Dictionary<string, string?> target)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            var text = ReadScalar(property.Value);
            if (text is not null) target[property.Name] = text;
        }
    }

    private static string? ReadScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };
}