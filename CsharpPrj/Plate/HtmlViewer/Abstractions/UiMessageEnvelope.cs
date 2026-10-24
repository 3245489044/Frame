#nullable enable

using System;
using System.Text.Json;

namespace UiBackend.Abstractions;

/// <summary>
/// 页面消息信封（唯一解析点）：把 postMessage 负载一次解析为「命令/事件 + 作用域 + 名称 + 负载文本」。
/// <para>命令队列（合并键/优先级）与事件桥（事件路由）共用本解析，禁各自再 <c>JsonDocument.Parse</c>
/// 各写一套字段口径（DUP-06 / SingleImplementation）。</para>
/// </summary>
/// <param name="Kind">命令/事件名（<c>cmd</c> 优先，其次 <c>event</c>，均无 ⇒ <c>message</c>）。</param>
/// <param name="EventName">事件名（<c>event</c> 字段；无 ⇒ null）。</param>
/// <param name="ControlName">控件名（<c>name</c> 字段；无 ⇒ null）。</param>
/// <param name="Scope">作用域（session / history / task / name 依次回落；无 ⇒ null）。</param>
/// <param name="PayloadText">负载文本（<c>payload</c> 为字符串则取之，否则取对象原文；非法/非对象 ⇒ 原始负载）。</param>
/// <param name="Raw">原始负载（审计/诊断用）。</param>
public sealed record UiMessageEnvelope(
    string Kind,
    string? EventName,
    string? ControlName,
    string? Scope,
    string? PayloadText,
    string Raw)
{
    /// <summary>无法归类时的命令名（非 JSON / 非对象 / 空负载）。</summary>
    public const string FallbackKind = "message";

    /// <summary>唯一解析入口：不抛（非法负载保留原文并按事件负载直传）。</summary>
    public static UiMessageEnvelope Parse(string? payload)
    {
        var raw = payload ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return new(FallbackKind, null, null, null, null, raw);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(FallbackKind, null, null, null, raw, raw);

            var eventName = Field(root, "event");
            var kind = Field(root, "cmd") ?? eventName ?? FallbackKind;
            var controlName = Field(root, "name");
            var scope = Field(root, "session") ?? Field(root, "history") ?? Field(root, "task") ?? controlName;
            var payloadText = root.TryGetProperty("payload", out var pl) && pl.ValueKind == JsonValueKind.String
                ? pl.GetString()
                : root.GetRawText();
            return new(kind, eventName, controlName, scope, payloadText, raw);
        }
        catch (JsonException)
        {
            return new(FallbackKind, null, null, null, raw, raw);
        }
    }

    /// <summary>命令/事件名匹配（大小写不敏感）。</summary>
    public bool Is(string kind) => string.Equals(Kind, kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>同键合并键 = 命令/事件 + 作用域（与旧键口径一致：<c>cmd|scope</c>）。</summary>
    public string MergeKey => $"{Kind}|{Scope}";

    private static string? Field(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
}
