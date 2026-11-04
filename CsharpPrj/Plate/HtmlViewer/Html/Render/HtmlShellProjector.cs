#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public static string RenderSelector(
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
                var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["type"] = key,
                    ["label"] = label,
                    ["currentView"] = currentKey,
                    ["active"] = isCurrent ? "1" : string.Empty,
                    ["selected"] = isCurrent ? "1" : string.Empty,
                };
                rows.Add(catalog?.Apply(rowComponent, model) ?? model);
            }
        }

        return HtmlTemplateEngine.Render(templates, "segGroup",
            new Dictionary<string, object?> { ["items"] = rows });
    }

    /// <summary>
    /// 渲染「上下分栏」壳（<c>splitShell</c> 模板）：上栏（<c>{nodes|raw}</c>）+ 下栏（<c>{history|raw}</c>）。
    /// 典型用途：观测面板（节点聚拢）叠于会话流之上；两栏片段由宿主已渲染好，本方法只做模板拼接。
    /// 缺模板 → 返回空串（调用方据此回退纯内容）。
    /// </summary>
    /// <param name="templates">模板集（含 <c>splitShell</c> 键）。</param>
    /// <param name="primaryHtml">上栏 HTML 片段（如节点面板）。</param>
    /// <param name="secondaryHtml">下栏 HTML 片段（如会话历史 / 对话流）。</param>
    public static string RenderPaneSplit(
        IReadOnlyDictionary<string, string> templates,
        string primaryHtml,
        string secondaryHtml)
        => HtmlTemplateEngine.Render(templates, "splitShell",
            new Dictionary<string, object?>
            {
                ["nodes"] = primaryHtml,
                ["history"] = secondaryHtml,
            });

    /// <summary>区域容器属性前缀（服务端注入；与 DomOps.Patch name 对齐，供区域化写回命中）。</summary>
    public const string RegionNamePrefix = "region-";

    /// <summary>视图根包装：根携带 <c>data-view-root</c> + 稳定键（视图键缺失 ⇒ 抛，禁止无根降级）。</summary>
    public static string RenderViewRoot(string shellHtml, string? viewKey)
    {
        if (string.IsNullOrWhiteSpace(viewKey))
            throw new ArgumentException("视图根必须携带视图键（无根降级已下线）", nameof(viewKey));
        return UiViewSkeleton.Wrap(viewKey!.Trim(), shellHtml);
    }


    /// <summary>
    /// 声明驱动的容器壳（视图作用域）：容器集来自视图程序（<c>containers[]</c>），内容按容器键供给；
    /// 未声明的容器不产出（结构上不存在 ⇒ 无需清理）。
    /// <para>框架只认结构位（<c>main</c> 为内容壳；<c>seg/head/subhead/aside/overlay</c> 为壳槽），零领域视图词。</para>
    /// </summary>
    public static string RenderContainerShell(
        IReadOnlyDictionary<string, string> templates,
        IReadOnlyList<UiViewContainer> containers,
        string selectorHtml,
        string title,
        string subtitle,
        string treeHtml,
        IReadOnlyDictionary<string, string> contents,
        IReadOnlyList<(string Key, string Html)> bubbles,
        string composerHtml,
        string overlayHtml,
        string? pollAttrs = null,
        string? refreshTitle = null,
        string? viewKey = null)
    {
        var present = new HashSet<string>(containers.Select(static c => c.Key), StringComparer.Ordinal);

        string Slot(string suffix) => RegionNamePrefix + suffix;
        string Html(string key) => contents.TryGetValue(key, out var value) ? value : string.Empty;

        var bubbleHtml = new StringBuilder();
        foreach (var section in bubbles ?? Array.Empty<(string Key, string Html)>()) bubbleHtml.Append(section.Html);

        var mainInner = new StringBuilder();
        foreach (var container in containers)
        {
            var slot = UiViewContainerSlots.Classify(container);
            if (slot == UiViewContainerSlot.Main || UiViewContainerSlots.IsShellSlot(slot)) continue;

            var body = slot == UiViewContainerSlot.Stream ? Html(container.Key) + bubbleHtml : Html(container.Key);
            mainInner.Append("<div class=\"region").Append(container.IsStream ? " stream" : string.Empty)
                .Append("\" data-name=\"").Append(container.Key).Append("\">").Append(body).Append("</div>");
        }

        var main = "<div class=\"region\" data-name=\"" + Slot("main") + "\">" + mainInner + "</div>";
        var selectorRegion = present.Contains(Slot("seg"))
            ? "<div class=\"seg region\" data-name=\"" + Slot("seg") + "\">" + selectorHtml + "</div>"
            : string.Empty;
        var titleRegion = present.Contains(Slot("head"))
            ? "<span data-name=\"" + Slot("head") + "\">" + title + "</span>"
            : title;
        var subtitleRegion = present.Contains(Slot("subhead"))
            ? "<span data-name=\"" + Slot("subhead") + "\">" + subtitle + "</span>"
            : subtitle;
        var treeRegion = present.Contains(Slot("aside"))
            ? "<div class=\"region\" data-name=\"" + Slot("aside") + "\">" + treeHtml + "</div>"
            : string.Empty;
        var overlayRegion = present.Contains(Slot("overlay"))
            ? "<div class=\"region\" data-name=\"" + Slot("overlay") + "\">" + overlayHtml + "</div>"
            : string.Empty;

        var html = HtmlTemplateEngine.Render(templates, "layoutShell",
            new Dictionary<string, object?>
            {
                ["selector"] = selectorRegion,
                ["title"] = titleRegion,
                ["subtitle"] = subtitleRegion,
                ["tree"] = treeRegion,
                ["history"] = main,
                ["overlay"] = overlayRegion,
                ["pollAttrs"] = pollAttrs ?? string.Empty,
                ["refreshTitle"] = refreshTitle ?? string.Empty,
            });

        return RenderViewRoot(html, viewKey);
    }

    /// <summary>页面视图宿主（<c>data-name=ui-root</c>）：唯一挂载点；整页首挂载包一层，视图切换只投影其子集。</summary>
    public static string RenderViewHost(string viewRootHtml)
        => "<div class=\"view-host\" data-name=\"" + UiViewSkeleton.DefaultRootName + "\">" + viewRootHtml + "</div>";

    /// <summary>气泡段包裹（单源）：段根带稳定 <c>data-key</c> ⇒ 流原语可按段键定点更新/追加。</summary>
    public static string WrapStreamSection(string key, string html)
        => "<div class=\"stream-item\" data-key=\"" + key + "\">" + html + "</div>";

    /// <summary>
    /// 容器内容装配（框架件，单一映射实现）：<c>containers[] × 壳槽 × 按容器键内容 ⇒ 容器键 → 片段</c>。
    /// <para>结构槽知识只在本类 + <see cref="UiViewContainerSlots"/>（域侧不再写 suffix <c>switch</c>）：
    /// 壳槽（seg/head/subhead/aside/composer/overlay）取 <paramref name="shellSlots"/>；
    /// 内容容器取 <paramref name="containerContents"/>；<c>main</c> 为结构包裹（非容器，不产）；
    /// 流容器由流原语承载（不产区域目标）。</para>
    /// <para>缺内容 ⇒ 产空串（与既有「空即空态/收起」口径一致，写目标仍由键控对齐消费）。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComposeContents(
        IReadOnlyList<UiViewContainer> containers,
        IReadOnlyDictionary<string, string> shellSlots,
        IReadOnlyDictionary<string, string> containerContents)
    {
        ArgumentNullException.ThrowIfNull(containers);
        var slots = shellSlots ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var contents = containerContents ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var composed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var container in containers)
        {
            var slot = UiViewContainerSlots.Classify(container);
            if (slot is UiViewContainerSlot.Main or UiViewContainerSlot.Stream) continue;

            var suffix = UiViewContainerSlots.Suffix(container.Key);
            var key = UiViewContainerSlots.IsShellSlot(slot) || slot == UiViewContainerSlot.Composer
                ? suffix
                : container.Key;

            if (slots.TryGetValue(key, out var slotHtml)) composed[container.Key] = slotHtml;
            else if (contents.TryGetValue(container.Key, out var contentHtml)) composed[container.Key] = contentHtml;
            else composed[container.Key] = string.Empty;
        }

        return composed;
    }
}
