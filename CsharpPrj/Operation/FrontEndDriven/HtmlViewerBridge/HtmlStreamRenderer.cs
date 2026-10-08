#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Share.Conversation;
using Share.Conversation.Rendering;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 通用会话消息流 HTML 渲染器 —— <see cref="IConversationRenderer"/> 的通用实现：
/// 会话历史 → 气泡式对话 body HTML 片段（供 htmlviewer 可信通道渲染）。
/// 纯函数式渲染：按角色布局（user 右蓝 / 其余左灰），附 轮次/角色/阶段 角标。
/// HTML 片段由 round/message 模板经 <see cref="HtmlTemplateEngine"/>（plate）渲染，C# 只构造行数据模型；
/// 所有文本经引擎 HTML 编码后入 DOM（XSS 防线在生产端）。
/// 模板源经构造注入延迟 <see cref="Func{TResult}"/> 解析（宿主可在运行时动态装配，如 Agent 读 AgentRuntimeUI.json 注入）；
/// 未注入/缺模板时输出内嵌空态兜底，不崩。
/// <para>历史归属：本文件原置于 Plate.HtmlViewer（UiBackend.Html），属 Conversation×plate(HtmlTemplateEngine) 的桥
/// （跨层胶水），现迁入 FrontEndDriven 连接层（真实组合根宿主层），使 plate 不再引用 Share.Conversation。</para>
/// </summary>
public sealed class HtmlStreamRenderer : IConversationRenderer
{
    private readonly HtmlStreamRendererOptions _options;
    private readonly Func<IReadOnlyDictionary<string, string>>? _templateProvider;

    public HtmlStreamRenderer(
        Func<IReadOnlyDictionary<string, string>>? templateProvider = null,
        HtmlStreamRendererOptions? options = null)
    {
        _options = options ?? new HtmlStreamRendererOptions();
        _templateProvider = templateProvider;
    }

    /// <summary>生效模板集：延迟解析（宿主注入）；null → 空集（缺模板由空态兜底）。</summary>
    private IReadOnlyDictionary<string, string> EffectiveTemplates
        => _templateProvider?.Invoke() ?? new Dictionary<string, string>();

    /// <summary>
    /// 渲染整段会话历史为 body HTML 片段；空历史输出空态文案。
    /// 层次化：按轮次（Round）分组输出 .round 区段（轮头 .round-header，含 data-round，供点击桥 selectHistory 客户端滚动聚焦）；
    /// 工作流中层（Tool 且带 Stage/SubAgent）以 &lt;details&gt; 折叠展示（.tool-layer，summary 显 Stage·SubAgent）；
    /// 角色差异化样式（user 右蓝 / agent 左灰 / error 红 / system 居中弱化）。
    /// 结构由 round/message 模板经引擎渲染，文本经引擎 HTML 编码后入 DOM（XSS 防线在生产端）。
    /// </summary>
    public string Render(IReadOnlyList<ConversationRenderItem> items)
    {
        var t = EffectiveTemplates;
        if (items is null || items.Count == 0)
        {
            // 空态：优先模板集 emptyP，未定义回退 Options.EmptyTemplate 语义
            if (t.TryGetValue("emptyP", out var emptyTpl))
                return HtmlTemplateEngine.RenderTemplate(
                    emptyTpl, t, new Dictionary<string, object?> { ["text"] = _options.EmptyText });
            return _options.EmptyTemplate.Replace("{text}", WebUtility.HtmlEncode(_options.EmptyText));
        }

        var sb = new StringBuilder(items.Count * 128);
        // 按轮次分组（Round 升序；Round=0 会话级归入"会话级"组）
        foreach (var group in items.GroupBy(m => m.Round).OrderBy(g => g.Key))
        {
            int round = group.Key;
            var row = new Dictionary<string, object?>
            {
                ["round"] = round.ToString(),
                ["roundTitle"] = round <= 0 ? "会话级" : $"第 {round} 轮",
                ["messages"] = GroupByNearestAnchor(group).Select(ToAnchorRow).ToList<object?>(),
            };
            sb.Append(HtmlTemplateEngine.Render(t, "round", row));
        }
        return sb.ToString();
    }

    /// <summary>构造单条消息行数据模型（供 message 模板绑定）。C# 只产原始数据（role/round/stage/subagent/text），
    /// 角色类/角标/summary 结构全部由 message 模板的条件绑定承担；文本经引擎 HTML 编码后入 DOM（XSS 防线在生产端）。
    /// <paramref name="isAnchor"/>=true 时置锚点位（模板据此选择"气泡 + 下拉栏附属"分支）。</summary>
    private Dictionary<string, object?> ToMessageRow(ConversationRenderItem m, bool isAnchor = false)
        => new Dictionary<string, object?>
        {
            ["role"] = m.Role.ToString().ToLowerInvariant(),
            ["round"] = m.Round.ToString(),
            ["stage"] = m.Stage ?? string.Empty,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["tool"] = IsToolLayer(m) ? "1" : string.Empty,
            ["prompt"] = IsPromptLayer(m) ? "1" : string.Empty,
            ["anchor"] = isAnchor ? "1" : string.Empty,
            ["text"] = m.Text,
        };

    /// <summary>是否工具/过程层（Tool 且带 Stage；无 Stage 的 Tool 走普通气泡分支）。</summary>
    private static bool IsToolLayer(ConversationRenderItem m)
        => m.Role == ConversationRole.Tool && !string.IsNullOrWhiteSpace(m.Stage);

    /// <summary>头提示层折叠分支（默认折叠、样式区分）：当 Options.PromptStagePrefix 配置时，stage 以该前缀开头的消息标记 prompt。
    /// 前缀由宿主注入（如 Agent 头提示层 "prompt:"），通用实现不硬编码领域前缀。</summary>
    private bool IsPromptLayer(ConversationRenderItem m)
        => _options.PromptStagePrefix is not null
            && m.Stage is not null
            && m.Stage.StartsWith(_options.PromptStagePrefix!, StringComparison.Ordinal);

    /// <summary>锚点判定（通用，不读领域 stage 语义）：user/assistant/error/system 为气泡锚点，其余（Tool 等过程项）可挂靠。</summary>
    private static bool IsAnchor(ConversationRole role)
        => role is ConversationRole.User or ConversationRole.Assistant
                 or ConversationRole.Error or ConversationRole.System;

    /// <summary>
    /// 近邻锚分组（R2，仅展示层）：轮内按**源序**扫描，过程项挂入其前方最近的锚点气泡的下拉栏；
    /// 锚点之前无锚点的项保持顶层（回退，内容零丢失）。输入恰被消费一次 ⇒ 顶层 + 挂靠 = 输入全集。
    /// </summary>
    private static List<(ConversationRenderItem Item, List<ConversationRenderItem> Attach)>
        GroupByNearestAnchor(IEnumerable<ConversationRenderItem> ordered)
    {
        var result = new List<(ConversationRenderItem, List<ConversationRenderItem>)>();
        List<ConversationRenderItem>? bucket = null;
        foreach (var m in ordered)
        {
            if (IsAnchor(m.Role))
            {
                bucket = new List<ConversationRenderItem>();
                result.Add((m, bucket));
            }
            else if (bucket is not null)
            {
                bucket.Add(m);
            }
            else
            {
                result.Add((m, new List<ConversationRenderItem>()));
            }
        }
        return result;
    }

    /// <summary>锚点行模型：消息字段 + 下拉栏附属（<c>attachCount</c>/<c>attach</c>）；无附属时不渲染下拉栏。</summary>
    private IReadOnlyDictionary<string, object?> ToAnchorRow(
        (ConversationRenderItem Item, List<ConversationRenderItem> Attach) entry)
    {
        var row = ToMessageRow(entry.Item, isAnchor: IsAnchor(entry.Item.Role));
        if (entry.Attach.Count > 0)
        {
            row["attachCount"] = entry.Attach.Count.ToString();
            row["attach"] = entry.Attach.Select(ToAttachRow).ToList<object?>();
        }
        return row;
    }

    /// <summary>附属项行模型（下拉栏子项）：与消息行同源字段，结构与编码由 attachItem 模板承担。</summary>
    private IReadOnlyDictionary<string, object?> ToAttachRow(ConversationRenderItem m)
        => new Dictionary<string, object?>
        {
            ["role"] = m.Role.ToString().ToLowerInvariant(),
            ["round"] = m.Round.ToString(),
            ["stage"] = m.Stage ?? string.Empty,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["tool"] = IsToolLayer(m) ? "1" : string.Empty,
            ["prompt"] = IsPromptLayer(m) ? "1" : string.Empty,
            ["text"] = m.Text,
        };
}

/// <summary>通用消息流 HTML 渲染器的可配置项（调用方注入）；缺省保持默认气泡输出。</summary>
public sealed record HtmlStreamRendererOptions
{
    /// <summary>空历史文案（用于空态）。</summary>
    public string EmptyText { get; init; } = "（空会话，尚无消息）";

    /// <summary>空态包裹模板，{text} 占位注入 <see cref="EmptyText"/>（模板集未定义 emptyP 时回退）。</summary>
    public string EmptyTemplate { get; init; } = "<p class=\"empty\">{text}</p>";

    /// <summary>「头提示层」折叠分支的 stage 前缀（如 Agent 的 "prompt:"）；null = 不启用该分支（消息统一按普通 tool 渲染）。
    /// 领域前缀由宿主注入，通用实现不硬编码。</summary>
    public string? PromptStagePrefix { get; init; }
}
