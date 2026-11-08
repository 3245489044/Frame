using System.Collections.Generic;
using System.Text.Json;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Utilities;

namespace Share.Conversation;

/// <summary>
/// 会话域 JSON 入口 —— **唯一 JSON 原语来源 = <c>Plate.JsonMethod</c>**（禁本域再直呼 <c>JsonDocument.Parse</c>
/// 或自建 <see cref="JsonSerializerOptions"/>）。
/// <para>解析策略：注释/尾随逗号容忍（<see cref="JsonTolerantReader.Tolerant"/> 单源）；根元素已 Clone。</para>
/// </summary>
internal static class ConversationJson
{
    /// <summary>解析根元素（非法即抛 <see cref="JsonException"/>，由消费方决定失败姿态）。</summary>
    /// <param name="json">JSON 文本。</param>
    public static JsonElement Root(string json) => JsonTolerantReader.ParseRoot(json, static root => root);

    /// <summary>容错解析根元素（空/非法 ⇒ null，不抛）。</summary>
    /// <param name="json">JSON 文本（可空）。</param>
    public static JsonElement? TryRoot(string? json) => JsonTolerantReader.TryRootOrNull(json);

    /// <summary>取对象段（缺失/非对象 ⇒ null）。</summary>
    /// <param name="element">父元素。</param>
    /// <param name="name">段名。</param>
    public static JsonElement? Section(JsonElement element, string name)
        => JsonTolerantReader.TrySection(element, name, out var section) ? section : null;

    /// <summary>序列化（平台选项单源）。</summary>
    /// <param name="value">对象。</param>
    /// <param name="context">选项上下文。</param>
    public static string Serialize<T>(T value, JsonContext context = JsonContext.Compact)
        => JsonSerializationService.Instance.Serialize(value, context);

    /// <summary>序列化（显式选项；选项须来自 <see cref="JsonSerializerFactory"/>）。</summary>
    /// <param name="value">对象。</param>
    /// <param name="options">选项。</param>
    public static string Serialize<T>(T value, JsonSerializerOptions options)
        => JsonSerializationService.Instance.Serialize(value, options);

    /// <summary>反序列化（平台选项单源；非法即抛）。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="context">选项上下文。</param>
    public static T? Deserialize<T>(string json, JsonContext context = JsonContext.Compact)
        => JsonSerializationService.Instance.Deserialize<T>(json, context);

    /// <summary>反序列化（显式选项；选项须来自 <see cref="JsonSerializerFactory"/>）。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="options">选项。</param>
    public static T? Deserialize<T>(string json, JsonSerializerOptions options)
        => JsonSerializationService.Instance.Deserialize<T>(json, options);
}
