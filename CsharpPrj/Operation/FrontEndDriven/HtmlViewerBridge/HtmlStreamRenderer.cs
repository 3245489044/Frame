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
/// HTML 片段由 sectionHeader/blockItem 模板经 <see cref="HtmlTemplateEngine"/>（plate）渲染，C# 只构造行数据模型；
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
    /// 层次化：按轮次（Round）分组输出 .section 区段（轮头 .section-header，含 data-round，供点击桥 selectHistory 客户端滚动聚焦）；
    /// 工作流中层（Tool 且带 Stage/SubAgent）以 &lt;details&gt; 折叠展示（.fold-layer，summary 显 Stage·SubAgent）；
    /// 角色差异化样式（user 右蓝 / agent 左灰 / error 红 / system 居中弱化）。
    /// 结构由 sectionHeader/blockItem 模板经引擎渲染，文本经引擎 HTML 编码后入 DOM（XSS 防线在生产端）。
    /// </summary>
    public string Render(IReadOnlyList<ConversationRenderItem> items)
        => string.Concat(RenderSections(items).Select(section => section.Html));

    /// <summary>
    /// 分段渲染（流面增量单源）：<see cref="Render"/> = 本方法各段按序拼接（禁两套拼接逻辑）。
    /// 段键：<c>head</c>（头提示层）/ <c>op</c>（操作流）/ <c>r{round}</c>（逐轮对话段）/ <c>empty</c>（空态）。
    /// 新轮 = 新段键（流尾追加）；同轮变更 = 同段键定点更新（调用方只交付变更段）。
    /// </summary>
    public IReadOnlyList<ConversationRenderSection> RenderSections(IReadOnlyList<ConversationRenderItem> items)
    {
        var t = EffectiveTemplates;
        if (items is null || items.Count == 0)
        {
            // 空态：优先模板集 emptyP，未定义回退 Options.EmptyTemplate 语义
            var emptyHtml = t.TryGetValue("emptyP", out var emptyTpl)
                ? HtmlTemplateEngine.RenderTemplate(
                    emptyTpl, t, new Dictionary<string, object?> { ["text"] = _options.EmptyText })
                : _options.EmptyTemplate.Replace("{text}", WebUtility.HtmlEncode(_options.EmptyText));
            return new[] { new ConversationRenderSection("empty", emptyHtml) };
        }

        // 通道分流：气泡流（Dialogue）承载 user/assistant/LLM 过程节点；操作流（Operation）承载
        // task 创建/管理等操作命令与回执，走顶部操作面板（非气泡）。关闭开关 ⇒ 逐字旧行为（全按气泡）。
        var dialogue = items;
        IReadOnlyList<ConversationRenderItem> operations = Array.Empty<ConversationRenderItem>();
        if (_options.SplitOperationChannel)
        {
            var split = items.Where(m => m.Channel != MessageChannel.Dialogue).ToList();
            if (split.Count > 0)
            {
                operations = split;
                dialogue = items.Where(m => m.Channel == MessageChannel.Dialogue).ToList();
            }
        }

        // 头提示层预抽取（与面序无关）：启用缓存稳定序时，把 prompt:* 段从对话流剔除，交 HeadPrompt 面单独编排。
        // 语义：头提示层顺序 = 快照 Bindings 序 = CacheBand 升序（顺序单一权威在段声明处，渲染层不重排）。
        IReadOnlyList<ConversationRenderItem> head = Array.Empty<ConversationRenderItem>();
        if (_options.HeadBandOrdering && _options.PromptStagePrefix is not null)
        {
            head = dialogue.Where(IsPromptLayer).ToList();
            if (head.Count > 0)
                dialogue = dialogue.Where(m => !IsPromptLayer(m)).ToList();
        }

        // 渲染面编排（声明式）：面序由 RenderSurfaceCatalog（CacheBand 派生）或宿主覆盖给出；
        // 渲染器只按序产出各面块 —— 禁在本处硬编码块拼接顺序（编排序单一权威 = 渲染面目录一处）。
        var sections = new List<ConversationRenderSection>(8);
        foreach (var surface in _options.SurfaceOrder ?? RenderSurfaceCatalog.Order)
        {
            switch (surface)
            {
                case RenderSurface.HeadPrompt:
                    if (head.Count > 0) sections.Add(new ConversationRenderSection("head", RenderHeadBandBlock(head, t)));
                    break; // 未启用/无段 ⇒ 该面空产（prompt:* 段随对话轮渲染，逐字旧行为）

                case RenderSurface.Operation:
                    if (operations.Count > 0) sections.Add(new ConversationRenderSection("op", RenderOperations(operations, t)));
                    break;

                case RenderSurface.Dialogue:
                    foreach (var group in OrderedRounds(dialogue))
                        sections.Add(new ConversationRenderSection("r" + group.Key, RenderRoundSection(group, t)));
                    break;
            }
        }
        return sections;
    }

    /// <summary>轮段序（Round 升序；Round=0 归「会话级」组）——单源：分段与整段拼接共用。</summary>
    private static IEnumerable<IGrouping<int, ConversationRenderItem>> OrderedRounds(
        IReadOnlyList<ConversationRenderItem> dialogue)
        => dialogue.GroupBy(m => m.Round).OrderBy(g => g.Key);

    /// <summary>单轮段渲染：Round 升序分组（Round=0 归「会话级」组），组内按近邻锚挂靠（R2）。
    /// 段根带 <c>data-key</c>（= <c>r{round}</c>）⇒ 流原语可按段键定点更新/追加（首挂载与增量同源）。</summary>
    private string RenderRoundSection(
        IGrouping<int, ConversationRenderItem> group, IReadOnlyDictionary<string, string> t)
    {
        int round = group.Key;
        var row = new Dictionary<string, object?>
        {
            ["round"] = round.ToString(),
            ["roundTitle"] = round <= 0 ? "会话级" : $"第 {round} 轮",
            ["messages"] = GroupByNearestAnchor(group).Select((e, gi) => ToAnchorRow(e, round, gi)).ToList<object?>(),
        };
        return HtmlTemplateEngine.Render(t, "sectionHeader", row);
    }

    /// <summary>
    /// 操作流渲染（非气泡）：<see cref="MessageChannel.Operation"/> 消息逐条落入顶部操作面板，
    /// 与对话气泡物理分离（任务创建/管理等命令与回执不再冒充 user/assistant 气泡）。
    /// <para>行承载（G5）：<c>{round,title,text,subagent}</c> —— 「分发到哪个子智能体」是**结构事实**（写侧单源），
    /// 呈现侧不得由 Role 二次猜测（<see cref="MessageChannel"/> 契约）。模板 <c>actionPanel</c> 缺失 ⇒
    /// 自足内联兜底（与模板路径**共享同一行模型**，禁双份构造）。</para>
    /// </summary>
    private static string RenderOperations(
        IReadOnlyList<ConversationRenderItem> operations, IReadOnlyDictionary<string, string> t)
    {
        var rows = operations
            .OrderBy(m => m.Round)
            .Select(ToOperationRow)
            .ToList<object?>();

        if (t.TryGetValue("actionPanel", out var panel) && !string.IsNullOrWhiteSpace(panel))
            return HtmlTemplateEngine.RenderTemplate(panel, t, new Dictionary<string, object?> { ["rows"] = rows });

        // 内联兜底 = 薄转发同一行模型（无 partial 依赖），字段集与模板路径恒等。
        var sb = new StringBuilder();
        sb.Append("<div class=\"op-panel\"><div class=\"op-panel-title\">操作 / 任务</div>");
        foreach (var row in rows)
        {
            var r = (IReadOnlyDictionary<string, object?>)row!;
            var badge = r["round"] + " · " + r["title"];
            var sub = r["subagent"] as string;
            sb.Append("<div class=\"op-row\"><span class=\"op-badge\">")
              .Append(WebUtility.HtmlEncode(badge));
            if (!string.IsNullOrWhiteSpace(sub))
            {
                sb.Append(" · ").Append(WebUtility.HtmlEncode(sub));
            }
            // F3 分发目标集（与模板路径字段集恒等）：空串 ⇒ 不产该段（负对照）
            var targets = r["targets"] as string;
            if (!string.IsNullOrWhiteSpace(targets))
            {
                sb.Append(" → ").Append(WebUtility.HtmlEncode(targets));
            }
            sb.Append("</span><div class=\"op-text\">")
              .Append(WebUtility.HtmlEncode(r["text"] as string ?? string.Empty))
              .Append("</div></div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// 操作行模型（**唯一构造点**，S3/I2 稳定键由渲染层派生）：<c>{round,title,text,subagent}</c>。
    /// <c>title</c> 只读 <see cref="ConversationRenderItem.Stage"/>（承载字段单源），缺省空串——
    /// 禁以 <see cref="ConversationRenderItem.Role"/> 兜底（SSOT：读侧/渲染侧只读本字段，禁按 Role 二次猜测）。
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ToOperationRow(ConversationRenderItem m)
        => new Dictionary<string, object?>
        {
            ["round"] = m.Round.ToString(),
            ["title"] = m.Stage ?? string.Empty,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(m.SubAgent) ? string.Empty : "1",
            // F3 分发目标集（单源事实）：空 ⇒ 空串（模板 {?showTargets} 判空不渲染该段；可见位由供数侧声明）。
            ["targets"] = m.Targets is { Count: > 0 } ? string.Join(" ", m.Targets) : string.Empty,
            ["showTargets"] = m.Targets is { Count: > 0 } ? "1" : string.Empty,
            ["text"] = m.Text ?? string.Empty,
        };


    /// <summary>
    /// 头提示层顺序块（CacheBand 升序 = 传入源序）：单组渲染（round=0 / 「会话级」），不参与 Round 分组。
    /// 仅当 <see cref="HtmlStreamRendererOptions.HeadBandOrdering"/> 开启且存在 prompt:* 段时调用。
    /// </summary>
    private static string RenderHeadBandBlock(
        IReadOnlyList<ConversationRenderItem> head, IReadOnlyDictionary<string, string> t)
    {
        var row = new Dictionary<string, object?>
        {
            ["round"] = "0",
            ["roundTitle"] = "会话级",
            ["messages"] = head
                .Select((m, i) => (Entry: (Item: m, Attach: new List<ConversationRenderItem>()), Index: i))
                .Select(x => (object?)HeadRow(x.Entry, x.Index))
                .ToList(),
        };
        return HtmlTemplateEngine.Render(t, "sectionHeader", row);
    }

    /// <summary>头提示层单行模型（无锚点语义；round=0）。</summary>
    private static Dictionary<string, object?> HeadRow(
        (ConversationRenderItem Item, List<ConversationRenderItem> Attach) entry, int index)
        => new Dictionary<string, object?>
        {
            ["key"] = "h:0:" + index,
            ["role"] = entry.Item.Role.ToString().ToLowerInvariant(),
            ["round"] = "0",
            ["stage"] = entry.Item.Stage ?? string.Empty,
            ["subagent"] = entry.Item.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(entry.Item.SubAgent) ? string.Empty : "1",
            ["tool"] = IsToolLayer(entry.Item) ? "1" : string.Empty,
            ["prompt"] = "1",
            ["anchor"] = string.Empty,
            ["text"] = entry.Item.Text,
        };

    /// <summary>构造单条消息行数据模型（供 message 模板绑定）。C# 只产原始数据（role/round/stage/subagent/text），
    /// 角色类/角标/summary 结构全部由 message 模板的条件绑定承担；文本经引擎 HTML 编码后入 DOM（XSS 防线在生产端）。
    /// <paramref name="isAnchor"/>=true 时置锚点位（模板据此选择"气泡 + 下拉栏附属"分支）。</summary>
    private Dictionary<string, object?> ToMessageRow(ConversationRenderItem m, bool isAnchor = false, string? key = null)
        => new Dictionary<string, object?>
        {
            ["key"] = key ?? string.Empty,
            ["role"] = m.Role.ToString().ToLowerInvariant(),
            ["round"] = m.Round.ToString(),
            ["stage"] = m.Stage ?? string.Empty,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(m.SubAgent) ? string.Empty : "1",
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

    /// <summary>锚点判定（通用，不读领域 stage 语义）：委托 <see cref="ConversationAnchors.IsAnchor"/>（单源）。</summary>
    private static bool IsAnchor(ConversationRole role) => ConversationAnchors.IsAnchor(role);

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

    /// <summary>锚点行模型：消息字段 + 下拉栏附属（<c>attachCount</c>/<c>attach</c>）；无附属时不渲染下拉栏。
    /// <paramref name="groupIndex"/> 参与派生稳定键（S3/I2：键由渲染层产出，禁作者手写），供 reconciler 按 key 复用节点。</summary>
    private IReadOnlyDictionary<string, object?> ToAnchorRow(
        (ConversationRenderItem Item, List<ConversationRenderItem> Attach) entry, int round, int groupIndex)
    {
        var key = "m:" + round + ":" + groupIndex;
        var row = ToMessageRow(entry.Item, isAnchor: IsAnchor(entry.Item.Role), key: key);
        if (entry.Attach.Count > 0)
        {
            row["attachCount"] = entry.Attach.Count.ToString();
            var classification = _options.AttachClassification;
            if (classification is { Groups.Count: > 0 })
            {
                var groups = BuildAttachGroups(entry.Attach, classification, round, groupIndex);
                row["attachGroups"] = groups;
                row["attachGroupCount"] = groups.Count.ToString();
                row["attachSummary"] = BuildAttachSummary(entry.Attach.Count, groups);
            }
            else
            {
                row["attachSummary"] = "过程 · " + entry.Attach.Count;
                row["attach"] = entry.Attach
                    .Select((m, ai) => ToAttachRow(m, "a:" + round + ":" + groupIndex + ":" + ai, string.Empty, null))
                    .ToList<object?>();
            }
        }
        return row;
    }

    /// <summary>
    /// 分类分组（声明驱动）：kind → 族（<see cref="AttachClassification.Kinds"/>），组序取宿主注入的
    /// <see cref="AttachGroupSpec.Order"/>（未命中 ⇒ 「其他」且回显 kind）。组内保持源序（seq），零丢失。
    /// </summary>
    private List<Dictionary<string, object?>> BuildAttachGroups(
        List<ConversationRenderItem> attach, AttachClassification classification, int round, int groupIndex)
    {
        var buckets = new Dictionary<string, List<ConversationRenderItem>>(StringComparer.Ordinal);
        foreach (var item in attach)
        {
            var kind = item.Stage ?? string.Empty;
            var groupKey = classification.Kinds.TryGetValue(kind, out var spec) && !string.IsNullOrWhiteSpace(spec.GroupKey)
                ? spec.GroupKey
                : classification.OtherGroupKey;
            if (!buckets.TryGetValue(groupKey, out var list))
            {
                list = new List<ConversationRenderItem>();
                buckets[groupKey] = list;
            }
            list.Add(item);
        }

        var groupOrder = classification.Groups.ToDictionary(g => g.Key, g => g, StringComparer.Ordinal);
        var ordered = buckets
            .OrderBy(pair => pair.Key == classification.OtherGroupKey
                ? int.MaxValue
                : groupOrder.TryGetValue(pair.Key, out var spec) ? spec.Order : int.MaxValue - 1)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<Dictionary<string, object?>>(ordered.Count);
        for (var gi = 0; gi < ordered.Count; gi++)
        {
            var pair = ordered[gi];
            var groupSpec = groupOrder.TryGetValue(pair.Key, out var declared)
                ? declared
                : string.Equals(pair.Key, classification.OtherGroupKey, StringComparison.Ordinal)
                    ? new AttachGroupSpec(classification.OtherGroupKey, classification.OtherLabel, int.MaxValue)
                    : new AttachGroupSpec(pair.Key, pair.Key, int.MaxValue - 1);
            var rows = new List<object?>(pair.Value.Count);
            for (var i = 0; i < pair.Value.Count; i++)
            {
                var message = pair.Value[i];
                var kind = message.Stage ?? string.Empty;
                var badge = classification.Kinds.TryGetValue(kind, out var kindSpec) ? kindSpec.Badge : null;
                rows.Add(ToAttachRow(
                    message, "a:" + round + ":" + groupIndex + ":" + gi + ":" + i, pair.Key, badge));
            }
            result.Add(new Dictionary<string, object?>
            {
                ["key"] = "g:" + round + ":" + groupIndex + ":" + gi,
                ["group"] = pair.Key,
                ["label"] = groupSpec.Label,
                ["count"] = pair.Value.Count.ToString(),
                ["items"] = rows,
            });
        }
        return result;
    }

    /// <summary>下拉栏摘要（分类概览，单源构造）：<c>过程 · N 项（工具 6 / LLM 2 / 阶段 3）</c>。</summary>
    private static string BuildAttachSummary(int total, IReadOnlyList<Dictionary<string, object?>> groups)
        => "过程 · " + total + " 项（"
           + string.Join(" / ", groups.Select(g => g["label"] + " " + g["count"]))
           + "）";

    /// <summary>附属项行模型（下拉栏子项）：与消息行同源字段，结构与编码由 attachItem 模板承担；
    /// <paramref name="key"/> 为渲染层派生的稳定键（供 reconciler 复用）；<paramref name="group"/>/
    /// <paramref name="badge"/> 为分类呈现字段（徽标空 ⇒ 模板回退显示 stage）。</summary>
    private IReadOnlyDictionary<string, object?> ToAttachRow(
        ConversationRenderItem m, string key, string group, string? badge)
        => new Dictionary<string, object?>
        {
            ["key"] = key,
            ["group"] = group,
            ["badge"] = badge ?? string.Empty,
            ["role"] = m.Role.ToString().ToLowerInvariant(),
            ["round"] = m.Round.ToString(),
            ["stage"] = m.Stage ?? string.Empty,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(m.SubAgent) ? string.Empty : "1",
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

    /// <summary>
    /// 渲染面编排序（声明式覆盖；null ⇒ <see cref="RenderSurfaceCatalog.Order"/>）。
    /// 面序 = 渲染块顺序的**唯一来源**（渲染器不硬编码拼接顺序）：宿主可注入自有声明（如把操作面板移到底部）。
    /// </summary>
    public IReadOnlyList<RenderSurface>? SurfaceOrder { get; init; }

    /// <summary>
    /// 头提示层按缓存带（CacheBand）顺序渲染开关：
    /// true ⇒ prompt:* 段整体前置为一个顺序块（传入源序 = CacheBand 升序），脱离 Round 分组一刀切；
    /// false（默认）⇒ 逐字旧行为（prompt:* 随 Round=0 归入「会话级」组）。权威: 顺序框架设计 §4.2/§9（单开关回退）。
    /// </summary>
    public bool HeadBandOrdering { get; init; }

    /// <summary>操作流 / 气泡流分离开关：true（默认）⇒ <see cref="MessageChannel.Operation"/> 消息渲染为顶部操作面板
    /// （非气泡），不进对话气泡；false ⇒ 旧行为（全部按角色气泡渲染，用于回退）。</summary>
    public bool SplitOperationChannel { get; init; } = true;

    /// <summary>
    /// 附属项分类表（宿主注入；null/空 ⇒ **逐字旧行为**：下拉栏为扁平列表）。
    /// 分类事实来自节点聚拢声明（kind → 族/徽标），本类型只承载呈现所需映射；通用渲染器不硬编码领域词表。
    /// </summary>
    public AttachClassification? AttachClassification { get; init; }
}

/// <summary>附属项分组（呈现）：<paramref name="Key"/> = 族键；<paramref name="Order"/> = 组序（升序）。</summary>
public sealed record AttachGroupSpec(string Key, string Label, int Order);

/// <summary>附属项类别（分类事实）：<paramref name="Kind"/> = 帧类别；<paramref name="GroupKey"/> = 所属族；
/// <paramref name="Badge"/> = 条目徽标（可空 ⇒ 模板回退显示 kind）。</summary>
public sealed record AttachKindSpec(string Kind, string GroupKey, string? Badge);

/// <summary>
/// 附属项分类表：<see cref="Groups"/>（组序/组名）+ <see cref="Kinds"/>（kind → 族/徽标）。
/// 未命中 <see cref="Kinds"/> 的 kind 归入 <see cref="OtherGroupKey"/>（可见、不丢弃）。
/// </summary>
public sealed record AttachClassification(
    IReadOnlyList<AttachGroupSpec> Groups,
    IReadOnlyDictionary<string, AttachKindSpec> Kinds,
    string OtherGroupKey = "other",
    string OtherLabel = "其他");
