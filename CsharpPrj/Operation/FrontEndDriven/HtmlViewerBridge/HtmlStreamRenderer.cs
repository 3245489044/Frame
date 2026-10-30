#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly ConversationFramePolicy? _policy;
    private readonly string? _degradedReason;

    public HtmlStreamRenderer(
        Func<IReadOnlyDictionary<string, string>>? templateProvider = null,
        HtmlStreamRendererOptions? options = null)
    {
        _options = options ?? new HtmlStreamRendererOptions();
        _templateProvider = templateProvider;
        // 单一语义表：宿主注入优先；否则把旧选项适配为同一策略（单批次兼容）。
        _policy = _options.Policy
            ?? ConversationFramePolicy.FromLegacy(_options.AttachGrouping, _options.AttachClassification);
        // 声明期望存在但不可用 ⇒ 可见降级（禁静默换语义）。
        _degradedReason = _options.RequireFramePolicy && _options.Policy is null
            ? "frame-policy-unavailable"
            : null;
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
            var emptySections = new List<ConversationRenderSection>(2);
            if (DegradedSection() is { } degradedEmpty) emptySections.Add(degradedEmpty);
            emptySections.Add(new ConversationRenderSection("empty", emptyHtml));
            return emptySections;
        }

        // 通道分流：Operation → 操作面板；其余（Dialogue/Process）→ 对话流。
        // Process = 过程帧（trace）：不再单独成“过程面板”，改由锚点气泡下拉栏承载（A1 单呈现路径）。
        var dialogue = items;
        IReadOnlyList<ConversationRenderItem> operations = Array.Empty<ConversationRenderItem>();
        if (_options.SplitOperationChannel)
        {
            var operationSplit = items.Where(m => m.Channel == MessageChannel.Operation).ToList();
            if (operationSplit.Count > 0)
            {
                operations = operationSplit;
                dialogue = items.Where(m => m.Channel != MessageChannel.Operation).ToList();
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
        if (DegradedSection() is { } degraded) sections.Insert(0, degraded);
        return sections;
    }

    /// <summary>可见降级段（声明期望存在但不可用）：不静默换语义，用户与日志均可归因。</summary>
    private ConversationRenderSection? DegradedSection()
    {
        var reason = _degradedReason;
        return reason is null
            ? null
            : new ConversationRenderSection(
                "degraded",
                "<p class=\"render-degraded\" data-reason=\"" + WebUtility.HtmlEncode(reason) + "\">渲染降级："
                + WebUtility.HtmlEncode(reason) + "（过程分组声明不可用，已按扁平呈现）</p>");
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
        var items = new List<ConversationRenderItem>(group);
        // 单一定位规则：终答过渡提升 + 过程项挂其前方最近锚点（无前锚 ⇒ 原位顶层）。
        var entries = AttachPlanner.PlanTopLevel(items, _policy);
        var row = new Dictionary<string, object?>
        {
            ["round"] = round.ToString(),
            ["roundTitle"] = round <= 0 ? "会话级" : $"第 {round} 轮",
            ["messages"] = entries.Select((e, gi) => ToAnchorRow(e, round, gi)).ToList<object?>(),
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
            ["answerTrace"] = IsTraceAnswer(m) ? "1" : string.Empty,
            ["answerTraceLabel"] = IsTraceAnswer(m)
                ? _policy?.Labels.AnswerTrace ?? string.Empty
                : string.Empty,
            ["text"] = m.Text,
        };

    /// <summary>是否“由过程帧过渡提升的终答”（来源角标可见；归档 assistant 不标）。</summary>
    private bool IsTraceAnswer(ConversationRenderItem m)
        => _policy is { } policy
           && m.Role == ConversationRole.Assistant
           && policy.IsTraceText(m.Stage);

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

    /// <summary>锚点行模型：消息字段 + 下拉栏附属（<c>attachCount</c>/<c>attachSummary</c>）。
    /// <paramref name="groupIndex"/> 参与派生稳定键（S3/I2：键由渲染层产出，禁作者手写）。</summary>
    private IReadOnlyDictionary<string, object?> ToAnchorRow(
        AttachEntry entry, int round, int groupIndex)
    {
        var key = "m:" + round + ":" + groupIndex;
        var row = ToMessageRow(entry.Item, isAnchor: IsAnchor(entry.Item.Role), key: key);
        if (entry.Attach.Count == 0) return row;

        row["attachCount"] = entry.Attach.Count.ToString();
        if (_policy is null)
        {
            // 未注入语义表 ⇒ 逐字旧行为：扁平下拉栏（零回归）。
            row["attachSummary"] = "过程 · " + entry.Attach.Count;
            row["attach"] = entry.Attach
                .Select((m, ai) => (object?)ToAttachRowSpec(
                    new AttachRowSpec(m.Stage ?? string.Empty, null, m, null, null),
                    "a:" + round + ":" + groupIndex + ":" + ai, string.Empty))
                .ToList<object?>();
            return row;
        }

        var plan = AttachPlanner.PlanGroups(entry.Attach, _policy);
        row["attachGroups"] = BuildGroupRows(plan, round, groupIndex);
        row["attachSummary"] = BuildAttachSummary(plan);
        return row;
    }

    /// <summary>组行模型（键由渲染层派生；结果行内嵌，不另占行）。</summary>
    private List<Dictionary<string, object?>> BuildGroupRows(
        AttachPlan plan, int round, int groupIndex)
    {
        var result = new List<Dictionary<string, object?>>(plan.Groups.Count);
        for (var gi = 0; gi < plan.Groups.Count; gi++)
        {
            var group = plan.Groups[gi];
            var items = new List<object?>(group.Rows.Count);
            for (var ri = 0; ri < group.Rows.Count; ri++)
            {
                items.Add(ToAttachRowSpec(
                    group.Rows[ri], "a:" + round + ":" + groupIndex + ":" + gi + ":" + ri, group.Kind));
            }

            result.Add(new Dictionary<string, object?>
            {
                ["key"] = "g:" + round + ":" + groupIndex + ":" + gi,
                ["group"] = group.Kind,
                ["label"] = group.Label,
                ["count"] = group.Rows.Count.ToString(CultureInfo.InvariantCulture),
                ["items"] = items,
            });
        }

        return result;
    }

    /// <summary>下拉栏摘要（计数单源 = <see cref="AttachPlan"/>）：组计数之和 ≡ 摘要条数；
    /// 帧数仅在“帧 ≠ 行”时显式补充（结果嵌套不另计）。</summary>
    private static string BuildAttachSummary(AttachPlan plan)
        => "过程 · " + plan.RowCount + " 项（"
           + string.Join(" / ", plan.Groups.Select(g => g.Label + " " + g.Rows.Count))
           + "）"
           + (plan.RowCount == plan.FrameCount ? string.Empty : " · " + plan.FrameCount + " 帧");

    /// <summary>附属项行模型（下拉栏子项）：结构与编码由 attachItem 模板承担；
    /// <paramref name="key"/> 为渲染层派生的稳定键（供 reconciler 复用）；徽标空 ⇒ 模板回退显示 stage。
    /// 只写模板消费的字段（R2：role/round/tool 等无消费者字段已删除）。</summary>
    private Dictionary<string, object?> ToAttachRowSpec(AttachRowSpec row, string key, string group)
    {
        var m = row.Source;
        var result = new Dictionary<string, object?>
        {
            ["key"] = key,
            ["group"] = group,
            ["badge"] = row.Badge ?? string.Empty,
            ["stage"] = row.Kind,
            ["subagent"] = m.SubAgent ?? string.Empty,
            ["showSubagent"] = string.IsNullOrWhiteSpace(m.SubAgent) ? string.Empty : "1",
            ["prompt"] = IsPromptLayer(m) ? "1" : string.Empty,
            ["text"] = m.Text,
        };
        if (row.ResultText is not null)
        {
            result["hasResult"] = "1";
            result["resultText"] = row.ResultText;
            result["resultLabel"] = row.ResultLabel ?? string.Empty;
            result["resultKey"] = key + "-result";
        }

        return result;
    }
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
    /// 过程帧**语义单表**（宿主注入，唯一注入面）：kind → 角色/徽标 + 组文案 + 终答身份。
    /// null ⇒ 由旧 <see cref="AttachGrouping"/> / <see cref="AttachClassification"/> 适配（单批次兼容）。
    /// </summary>
    public ConversationFramePolicy? Policy { get; init; }

    /// <summary>是否要求语义表：true 且 <see cref="Policy"/> 为空 ⇒ 可见降级（禁静默换语义）。</summary>
    public bool RequireFramePolicy { get; init; }

    /// <summary>
    /// 附属项分类表（宿主注入；null/空 ⇒ **逐字旧行为**：下拉栏为扁平列表）。
    /// 分类事实来自节点聚拢声明（kind → 族/徽标），本类型只承载呈现所需映射；通用渲染器不硬编码领域词表。
    /// </summary>
    public AttachClassification? AttachClassification { get; init; }

    /// <summary>
    /// 过程帧旧分组策略（**兼容适配**）：仅用于把旧选项转成单一语义表（<see cref="Policy"/>）；
    /// 生产宿主应直接注入 <see cref="Policy"/>。null 且无分类 ⇒ 扁平下拉栏（逐字旧行为）。
    /// </summary>
    public AttachGrouping? AttachGrouping { get; init; }
}

/// <summary>
/// 过程帧顺序分组策略（声明驱动；通用渲染器不硬编码领域词表）：
/// <paramref name="BoundaryKinds"/> = 开新组的边界 kind（如 llm-iteration）；
/// <paramref name="TrailerKinds"/> = 收束组 kind（done/cost/usage…）；
/// <paramref name="AnswerKinds"/> = 终答候选 kind（无归档 assistant 时提升为气泡）；
/// <paramref name="CallKinds"/> / <paramref name="ResultKinds"/> = 结果嵌套配对词表；
/// 其余为组名/角标文案（由宿主经 loc 声明注入）。
/// </summary>
public sealed record AttachGrouping(
    IReadOnlySet<string> BoundaryKinds,
    IReadOnlySet<string> TrailerKinds,
    IReadOnlySet<string> AnswerKinds,
    IReadOnlySet<string> CallKinds,
    IReadOnlySet<string> ResultKinds,
    string LeadLabel,
    string IterationLabel,
    string TrailerLabel,
    string AnswerTraceLabel);

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
