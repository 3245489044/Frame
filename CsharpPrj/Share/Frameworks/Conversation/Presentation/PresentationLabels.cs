#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Presentation;

/// <summary>标签来源（loc 坐标 或 声明字面文本；二者取其一）。</summary>
/// <param name="Loc">宿主 loc 坐标（产品文案单源）。</param>
/// <param name="Text">声明字面文本（无 loc 时的可见来源）。</param>
public sealed record LabelSource(string? Loc, string? Text)
{
    /// <summary>取值（无声明 ⇒ null，调用方可见降级）。</summary>
    public string? Value => !string.IsNullOrWhiteSpace(Text) ? Text : null;
}

/// <summary>
/// **分组/摘要标签声明**（产品策略单源：<c>ui/labels.json</c>）：
/// 标签键 → loc 坐标（或字面文本）；框架只按标签键取值，不含任何领域文案。
/// </summary>
public sealed record PresentationLabels(IReadOnlyDictionary<string, LabelSource> Entries)
{
    /// <summary>标签键（结构词汇，唯一来源）。</summary>
    public static class Keys
    {
        /// <summary>段首前导组标签。</summary>
        public const string Lead = "lead";

        /// <summary>迭代组标签格式。</summary>
        public const string Iteration = "iteration";

        /// <summary>结算组标签。</summary>
        public const string Trailer = "trailer";

        /// <summary>过程文本标签（角标）。</summary>
        public const string Trace = "trace";

        /// <summary>结算后组标签。</summary>
        public const string PostTrailer = "postTrailer";

        /// <summary>其它组标签。</summary>
        public const string Other = "other";

        /// <summary>会话级标题。</summary>
        public const string SessionScope = "sessionScope";

        /// <summary>轮标题格式。</summary>
        public const string RoundTitle = "roundTitle";

        /// <summary>操作面板标题。</summary>
        public const string OperationPanelTitle = "operationPanelTitle";

        /// <summary>前导摘要格式。</summary>
        public const string LeadSummary = "leadSummary";

        /// <summary>附属栏标签。</summary>
        public const string AttachLabel = "attachLabel";

        /// <summary>附属栏摘要格式。</summary>
        public const string AttachSummary = "attachSummary";

        /// <summary>附属组计数格式。</summary>
        public const string AttachGroupCount = "attachGroupCount";

        /// <summary>附属摘要帧数格式。</summary>
        public const string AttachSummaryFrames = "attachSummaryFrames";

        /// <summary>附属摘要文本数格式。</summary>
        public const string AttachSummaryText = "attachSummaryText";

        /// <summary>结算后标签格式。</summary>
        public const string PostTrailerFormat = "postTrailerFormat";

        /// <summary>降级文案格式。</summary>
        public const string Degraded = "degraded";
    }

    /// <summary>空声明。</summary>
    public static readonly PresentationLabels Empty =
        new(new Dictionary<string, LabelSource>(StringComparer.Ordinal));

    /// <summary>声明是否可用。</summary>
    public bool IsDeclared => Entries.Count > 0;

    /// <summary>按标签键取值（未声明 ⇒ null，可见降级）。</summary>
    /// <param name="key">标签键（<see cref="Keys"/>）。</param>
    public LabelSource? Of(string key) => Entries.TryGetValue(key, out var source) ? source : null;

    /// <summary>
    /// 解析声明 JSON。形制：<c>{ "labels": { "&lt;键&gt;": { "loc": "text_x" } | { "text": "…" } } }</c>
    /// </summary>
    /// <param name="json">声明 JSON 文本。</param>
    public static PresentationLabels Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var entries = new Dictionary<string, LabelSource>(StringComparer.Ordinal);
        if (!root.TryGetProperty("labels", out var labelMap) || labelMap.ValueKind != JsonValueKind.Object)
        {
            return new PresentationLabels(entries);
        }

        foreach (var entry in labelMap.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                entries[entry.Name] = new LabelSource(entry.Value.GetString(), null);
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object) continue;
            var loc = ReadString(entry.Value, "loc");
            var text = ReadString(entry.Value, "text");
            if (!string.IsNullOrWhiteSpace(loc) || !string.IsNullOrWhiteSpace(text))
            {
                entries[entry.Name] = new LabelSource(loc, text);
            }
        }

        return new PresentationLabels(entries);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
