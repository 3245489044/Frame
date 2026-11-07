#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modules.Parsing.Markup;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 通用页面布局壳投影器 —— 把「布局壳模板（layoutShell）+ 切换器（seg）+ 浮层拼接（overlay）」的通用模板投影下沉到通用框架：
/// 纯模板投影 + 片段拼接，**零领域类型依赖**（不感知视图枚举 / 业务数据模型）。
/// 切换器项（key/label）与领域树/历史片段由宿主提供；布局结构由模板承担（改模板即改 UI，不改 C#）。
/// [下沉自 Agent.Launcher ConversationPageRenderer 的 Render/RenderSelector 通用部分，已通用化]
/// </summary>
public static class HtmlShellProjector
{
    /// <summary>
    /// 渲染切换器（segGroup 模板）：<paramref name="items"/> 为 (键, 标签) 列表，当前项（= <paramref name="currentKey"/>）标 .active。
    /// <para>
    /// 选中态同时投影为 <c>selected</c>（配合模板 <c>data-select-slot="view"</c> + <c>{?selected} data-selected{/}</c>），
    /// 使「当前作用域」成为可回投的选择态（页面回投 <c>uiState.select</c> 的 view 槽），
    /// 不再依赖后端进程级全局槽猜当前视图。
    /// </para>
    /// </summary>
    public static UiMarkup RenderSelector(
        IReadOnlyDictionary<string, string> templates,
        IReadOnlyList<(string Key, string Label)> items,
        string currentKey,
        UiComponentCatalog? catalog = null,
        string? rowComponent = null)
    {
        var rows = new List<object?>(items?.Count ?? 0);
        if (items is not null)
        {
            foreach (var (key, label) in items)
            {
                var isCurrent = key == currentKey;
                var model = UiModel.New();
                model["type"] = key;
                model["label"] = label;
                model["currentView"] = currentKey;
                model["active"] = UiModel.Flag(isCurrent);
                model["selected"] = UiModel.Flag(isCurrent);
                rows.Add(catalog?.Apply(rowComponent, model) ?? model);
            }
        }

        return HtmlTemplateEngine.RenderMarkup(templates, "segGroup",
            new Dictionary<string, object?> { ["items"] = rows });
    }

    /// <summary>
    /// 渲染「上下分栏」壳（<c>splitShell</c> 模板）：上栏（<c>{nodes|raw}</c>）+ 下栏（<c>{history|raw}</c>）。
    /// 典型用途：观测面板（节点聚拢）叠于会话流之上；两栏片段由宿主已渲染好，本方法只做模板拼接。
    /// 缺模板 → 返回空片段（调用方据此回退纯内容）。
    /// </summary>
    /// <param name="templates">模板集（含 <c>splitShell</c> 键）。</param>
    /// <param name="primary">上栏片段（如节点面板）。</param>
    /// <param name="secondary">下栏片段（如会话历史 / 对话流）。</param>
    public static UiMarkup RenderPaneSplit(
        IReadOnlyDictionary<string, string> templates,
        UiMarkup primary,
        UiMarkup secondary)
        => HtmlTemplateEngine.RenderMarkup(templates, "splitShell",
            new Dictionary<string, object?>
            {
                ["nodes"] = primary,
                ["history"] = secondary,
            });

    /// <summary>视图根包装：根携带 <c>data-view-root</c> + 稳定键（视图键缺失 ⇒ 抛，禁止无根降级）。</summary>
    public static string RenderViewRoot(string shellHtml, string? viewKey)
    {
        if (string.IsNullOrWhiteSpace(viewKey))
            throw new ArgumentException("视图根必须携带视图键（无根降级已下线）", nameof(viewKey));
        return UiViewSkeleton.Wrap(viewKey!.Trim(), shellHtml);
    }


    /// <summary>壳槽位值（**类型化**：片段 vs 纯文本在编译期分开）。整页壳与区域节点装配共用本记录 ⇒ 单源。</summary>
    public sealed record UiShellSlots(
        UiMarkup Selector,
        UiText Title,
        UiMarkup Subtitle,
        UiMarkup Tree,
        UiMarkup Composer,
        UiMarkup Overlay,
        UiMarkup? PollAttrs = null)
    {
        /// <summary>空壳（未声明的槽全部为空）。</summary>
        public static UiShellSlots Empty { get; } = new(
            UiMarkup.Empty, new UiText(string.Empty), UiMarkup.Empty,
            UiMarkup.Empty, UiMarkup.Empty, UiMarkup.Empty);
    }

    /// <summary>
    /// 声明驱动的容器壳（视图作用域）：容器集来自视图程序（<c>containers[]</c>），内容按容器键供给；
    /// 未声明的容器不产出（结构上不存在 ⇒ 无需清理）。
    /// <para>框架只认结构位（<c>main</c> 为内容壳；<c>seg/head/subhead/aside/overlay</c> 为壳槽），零领域视图词。</para>
    /// <para>槽位值类型化（<see cref="UiShellSlots"/>）：纯文本槽只能给 <see cref="UiText"/>，片段槽只能给
    /// <see cref="UiMarkup"/> ⇒ 「片段当文本再编码」在编译期不可表达。</para>
    /// </summary>
    public static string RenderContainerShell(
        IReadOnlyDictionary<string, string> templates,
        IReadOnlyList<UiViewContainer> containers,
        UiShellSlots slots,
        IReadOnlyDictionary<string, UiMarkup> contents,
        IReadOnlyList<(string Key, UiNode Root)> bubbles,
        string? refreshTitle = null,
        string? viewKey = null,
        IReadOnlyDictionary<string, SurfaceContainerMarkup>? markup = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var present = new HashSet<string>(containers.Select(static c => c.Key), StringComparer.Ordinal);

        string Slot(string suffix) => UiViewContainerSlots.RegionKey(suffix);
        UiMarkup Html(string key) => contents is not null && contents.TryGetValue(key, out var value) ? value : UiMarkup.Empty;

        string Block(string key, string fallbackClass, UiMarkup body)
        {
            var structure = markup is not null && markup.TryGetValue(key, out var found) ? found : null;
            var tag = string.IsNullOrWhiteSpace(structure?.Tag) ? "div" : structure!.Tag;
            var cssClass = string.Join(" ", new[] { fallbackClass, structure?.Class }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var attrs = new MarkupAttrBatch().Class(cssClass, fallbackClass).Name(key);
            if (structure is not null)
            {
                foreach (var pair in structure.Attributes) attrs.Data(pair.Key, pair.Value);
            }

            return MarkupTag.Element(tag, attrs, body.Html);
        }

        var bubbleHtml = new StringBuilder();
        foreach (var section in bubbles ?? Array.Empty<(string Key, UiNode Root)>())
            bubbleHtml.Append(UiFragmentWriter.Serialize(section.Root));

        var bubbleFragment = UiMarkup.Trusted(bubbleHtml.ToString());
        var mainInner = new StringBuilder();
        foreach (var container in containers)
        {
            var slot = UiViewContainerSlots.Classify(container);
            if (slot == UiViewContainerSlot.Main || UiViewContainerSlots.IsShellSlot(slot)) continue;

            // 取值来源单源 = HasShellSource（位置与来源两 predicate 分离）：composer 在主区内但取壳槽值。
            var body = UiViewContainerSlots.HasShellSource(slot)
                ? ShellSlotValue(slots, slot)
                : slot == UiViewContainerSlot.Stream
                    ? UiMarkup.Concat(Html(container.Key), bubbleFragment)
                    : Html(container.Key);
            mainInner.Append(Block(
                container.Key,
                container.IsStream ? "region stream" : "region",
                body));
        }

        // 未声明壳槽的可见回退：文本类槽（head/subhead）缺容器时不得整段消失（结构性槽不声明即不存在）。
        var undeclaredSlot = new StringBuilder();
        if (!present.Contains(Slot("head")) && !slots.Title.IsEmpty)
            undeclaredSlot.Append(UndeclaredSlot(Slot("head"), MarkupTag.Text(slots.Title)));
        if (!present.Contains(Slot("subhead")) && !slots.Subtitle.IsEmpty)
            undeclaredSlot.Append(UndeclaredSlot(Slot("subhead"), ShellSlotValue(slots, UiViewContainerSlot.SubHead).Html));

        var main = MarkupBlock.Render("div", "region", "region", Slot("main"),
            undeclaredSlot.Append(mainInner).ToString());
        var selectorRegion = present.Contains(Slot("seg"))
            ? Block(Slot("seg"), "seg region", slots.Selector)
            : string.Empty;
        var titleRegion = present.Contains(Slot("head"))
            ? MarkupPresets.LabelledSpan(Slot("head"), ShellSlotValue(slots, UiViewContainerSlot.Head).Html)
            : string.Empty;
        var subtitleRegion = present.Contains(Slot("subhead"))
            ? MarkupPresets.LabelledSpan(Slot("subhead"), ShellSlotValue(slots, UiViewContainerSlot.SubHead).Html)
            : string.Empty;
        var treeRegion = present.Contains(Slot("aside"))
            ? Block(Slot("aside"), "region", slots.Tree)
            : string.Empty;
        var overlayRegion = present.Contains(Slot("overlay"))
            ? Block(Slot("overlay"), "region", slots.Overlay)
            : string.Empty;

        var html = HtmlTemplateEngine.Render(templates, "layoutShell",
            new Dictionary<string, object?>
            {
                ["selector"] = UiMarkup.Trusted(selectorRegion),
                ["title"] = UiMarkup.Trusted(titleRegion),
                ["subtitle"] = UiMarkup.Trusted(subtitleRegion),
                ["tree"] = UiMarkup.Trusted(treeRegion),
                ["history"] = UiMarkup.Trusted(main),
                ["overlay"] = UiMarkup.Trusted(overlayRegion),
                ["pollAttrs"] = slots.PollAttrs ?? UiMarkup.Empty,
                ["refreshTitle"] = refreshTitle ?? string.Empty,
            });

        return RenderViewRoot(html, viewKey);
    }

    /// <summary>页面视图宿主（<c>data-name=ui-root</c>）：唯一挂载点；整页首挂载包一层，视图切换只投影其子集。</summary>
    public static string RenderViewHost(string viewRootHtml)
        => MarkupTag.Element(
            "div",
            new MarkupAttrBatch().Class("view-host", "view-host").Name(UiViewSkeleton.DefaultRootName),
            viewRootHtml);

    /// <summary>气泡段包裹（单源）：段根带稳定 <c>data-key</c> ⇒ 流原语可按段键定点更新/追加。</summary>
    public static string WrapStreamSection(string key, UiMarkup html)
        => MarkupPresets.StreamItem(key, html.Html);

    /// <summary>
    /// 容器内容装配（框架件，单一映射实现）：<c>containers[] × 壳槽 × 按容器键内容 ⇒ 容器键 → 片段</c>。
    /// <para>结构槽知识只在本类 + <see cref="UiViewContainerSlots"/>（域侧不再写 suffix <c>switch</c>）：
    /// 壳槽（seg/head/subhead/aside/composer/overlay）取 <paramref name="shellSlots"/>；
    /// 内容容器取 <paramref name="containerContents"/>；<c>main</c> 为结构包裹（非容器，不产）；
    /// 流容器由流原语承载（不产区域目标）。</para>
    /// <para>缺内容 ⇒ 产空片段（与既有「空即空态/收起」口径一致，写目标仍由键控对齐消费）。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComposeContents(
        IReadOnlyList<UiViewContainer> containers,
        UiShellSlots shellSlots,
        IReadOnlyDictionary<string, UiMarkup> containerContents,
        IReadOnlyDictionary<string, SurfaceContainerMarkup>? markup = null)
        => ComposeContentNodes(containers, shellSlots, containerContents, markup)
            .ToDictionary(static pair => pair.Key, static pair => UiFragmentWriter.Serialize(pair.Value), StringComparer.Ordinal);

    /// <summary>
    /// 容器内容装配（**节点出口**，唯一实现）：<c>containers[] × 壳槽 × 按容器键内容 ⇒ 容器键 → 节点</c>。
    /// <para>壳槽/内容为已渲染片段（受信模板产物）⇒ <see cref="UiNode.Markup"/> 承载，不经 HTML→节点回解析；
    /// 仅带 <see cref="SurfaceContainerMarkup"/> 的容器外套结构 <c>&lt;tag&gt;</c> 节点（属性序 = class → data-name → data-*）。</para>
    /// <para>槽位取值只经 <see cref="ShellSlotValue"/>（与 <see cref="RenderContainerShell"/> 同源）⇒ 两条路径不可能分叉。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, UiNode> ComposeContentNodes(
        IReadOnlyList<UiViewContainer> containers,
        UiShellSlots shellSlots,
        IReadOnlyDictionary<string, UiMarkup>? containerContents,
        IReadOnlyDictionary<string, SurfaceContainerMarkup>? markup = null)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(shellSlots);
        var contents = containerContents ?? new Dictionary<string, UiMarkup>(StringComparer.Ordinal);
        var composed = new Dictionary<string, UiNode>(StringComparer.Ordinal);

        foreach (var container in containers)
        {
            var slot = UiViewContainerSlots.Classify(container);
            if (slot is UiViewContainerSlot.Main or UiViewContainerSlot.Stream) continue;

            var value = UiViewContainerSlots.HasShellSource(slot)
                ? ShellSlotValue(shellSlots, slot)
                : contents.TryGetValue(container.Key, out var contentHtml) ? contentHtml : UiMarkup.Empty;
            // 无结构标记 ⇒ 保持旧契约（纯内容片段）；有标记 ⇒ 只对已声明容器包裹（零回归）。
            composed[container.Key] = markup is not null && markup.TryGetValue(container.Key, out var structure)
                ? WrapNode(container.Key, value, structure)
                : UiNode.Markup(value);
        }

        return composed;
    }

    /// <summary>壳槽 → 类型化值（**唯一映射**；纯文本位在这里编码一次）。</summary>
    private static UiMarkup ShellSlotValue(UiShellSlots slots, UiViewContainerSlot slot) => slot switch
    {
        UiViewContainerSlot.Seg => slots.Selector,
        UiViewContainerSlot.Head => UiMarkup.Trusted(MarkupTag.Text(slots.Title)),
        UiViewContainerSlot.SubHead => slots.Subtitle,
        UiViewContainerSlot.Aside => slots.Tree,
        UiViewContainerSlot.Composer => slots.Composer,
        UiViewContainerSlot.Overlay => slots.Overlay,
        _ => UiMarkup.Empty,
    };

    /// <summary>未声明壳槽的可见回退（文本类槽不得静默丢弃；结构性槽不声明即不存在）。</summary>
    private static string UndeclaredSlot(string key, string inner)
        => MarkupTag.Element(
            "div",
            new MarkupAttrBatch().Class("shell-title", "shell-title").Name(key).Data("degrade", "slot-undeclared"),
            inner);

    private static UiNode WrapNode(string key, UiMarkup html, SurfaceContainerMarkup? structure)
    {
        var tag = string.IsNullOrWhiteSpace(structure?.Tag) ? "div" : structure!.Tag;
        // 类名去重：声明类若已含 region（如 "region panel"）不得拼成 "region region panel"。
        var tokens = new List<string> { "region" };
        if (structure is not null)
        {
            tokens.AddRange(structure.Class.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var cssClass = string.Join(" ", tokens.Distinct(StringComparer.Ordinal));
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["class"] = cssClass,
            ["data-name"] = key,
        };
        if (structure is not null)
        {
            foreach (var pair in structure.Attributes) attributes["data-" + pair.Key] = pair.Value;
        }

        return UiNode.Element(tag, attributes: attributes, children: new[] { UiNode.Markup(html) });
    }
}
