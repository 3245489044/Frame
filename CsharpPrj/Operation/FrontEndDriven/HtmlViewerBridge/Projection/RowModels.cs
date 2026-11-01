#nullable enable

using System.Collections.Generic;
using System.Globalization;
using Share.Conversation;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 行模型构造（**唯一构造点**）：模板只消费本类产出的字段集；禁投影器各自拼字典。
/// </summary>
internal static class RowModels
{
    /// <summary>消息行（blockItem 绑定字段集）。</summary>
    /// <param name="item">渲染项。</param>
    /// <param name="context">投影上下文。</param>
    /// <param name="isAnchor">是否锚点位（气泡 + 下拉栏分支）。</param>
    /// <param name="key">稳定键（渲染层派生；空 ⇒ 模板不参与差分）。</param>
    /// <param name="blankProcessFlags">折叠形态：清空 tool/prompt 位（前导行按折叠行呈现）。</param>
    public static Dictionary<string, object?> Message(
        ConversationRenderItem item,
        ConversationProjectionContext context,
        bool isAnchor,
        string key,
        bool blankProcessFlags = false)
    {
        var prompt = blankProcessFlags ? string.Empty : context.IsPrompt(item) ? "1" : string.Empty;
        var presentation = context.PresentationOf(item);
        var badge = context.HasAnswerBadge(item);

        return new Dictionary<string, object?>
        {
            ["key"] = key,
            ["role"] = item.Role.ToString().ToLowerInvariant(),
            ["round"] = item.Round.ToString(CultureInfo.InvariantCulture),
            ["stage"] = item.Stage ?? string.Empty,
            ["subagent"] = item.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(item.SubAgent) ? string.Empty : "1",
            ["prompt"] = prompt,
            ["anchor"] = isAnchor ? "1" : string.Empty,
            ["presentation"] = presentation.ClassId,
            ["presentationOrigin"] = presentation.Origin.ToString().ToLowerInvariant(),
            ["presentationUnknown"] = presentation.Origin == PresentationOrigin.Unknown ? "1" : string.Empty,
            ["class"] = context.StyleOf(item),
            ["template"] = context.TemplateOf(item),
            ["badge"] = context.Policy?.BadgeOf(item.Stage) ?? string.Empty,
            ["answerTrace"] = badge ? "1" : string.Empty,
            ["answerTraceLabel"] = badge ? context.Labels.Trace : string.Empty,
            ["text"] = item.Text,
        };
    }

    /// <summary>附属行模型（attachItem 绑定字段集）；只写模板消费的字段。</summary>
    public static Dictionary<string, object?> AttachRow(
        AttachRowSpec row,
        string key,
        string group,
        ConversationProjectionContext context)
    {
        var source = row.Source;
        var model = new Dictionary<string, object?>
        {
            ["key"] = key,
            ["group"] = group,
            ["badge"] = row.Badge ?? string.Empty,
            ["stage"] = row.Kind,
            ["subagent"] = source.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(source.SubAgent) ? string.Empty : "1",
            ["prompt"] = context.IsPrompt(source) ? "1" : string.Empty,
            ["text"] = source.Text,
        };

        if (row.ResultText is not null)
        {
            model["hasResult"] = "1";
            model["resultText"] = row.ResultText;
            model["resultLabel"] = row.ResultLabel ?? string.Empty;
            model["resultKey"] = FragmentKeys.Result(key);
        }

        return model;
    }

    /// <summary>
    /// 操作行模型：<c>title</c> 只读 <see cref="ConversationRenderItem.Stage"/>（承载字段单源），
    /// 禁以 Role 兜底二次猜测。
    /// </summary>
    public static Dictionary<string, object?> Operation(ConversationRenderItem item)
        => new()
        {
            ["round"] = item.Round.ToString(CultureInfo.InvariantCulture),
            ["title"] = item.Stage ?? string.Empty,
            ["subagent"] = item.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(item.SubAgent) ? string.Empty : "1",
            ["targets"] = item.Targets is { Count: > 0 } ? string.Join(" ", item.Targets) : string.Empty,
            ["showTargets"] = item.Targets is { Count: > 0 } ? "1" : string.Empty,
            ["text"] = item.Text ?? string.Empty,
        };

    /// <summary>头提示层行（无锚点语义；round=0）。</summary>
    public static Dictionary<string, object?> Head(ConversationRenderItem item, int index)
        => new()
        {
            ["key"] = FragmentKeys.Head(index),
            ["role"] = item.Role.ToString().ToLowerInvariant(),
            ["round"] = "0",
            ["stage"] = item.Stage ?? string.Empty,
            ["subagent"] = item.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(item.SubAgent) ? string.Empty : "1",
            ["prompt"] = "1",
            ["anchor"] = string.Empty,
            ["text"] = item.Text,
        };
}

