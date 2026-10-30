#nullable enable

using System.Collections.Generic;

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

    /// <summary>渲染布局壳（layoutShell 模板）：左 tree + 右 content + 独立浮层区 overlay，顶部 selector。
    /// <para>P2 区域化（I5）：把左侧树 / 右侧内容各包一层具名区域容器（<c>data-name=region-aside|region-main</c>），
    /// 使写回层可按区域 patch（<c>DomOps.Patch name=region-*</c>）而非整体替换；首挂载后壳稳定（I6），区域容器不随内容重建。</para>
    /// <para>E-8 外壳区域化：选区 / 标题 / 副标题亦为独立可写区域（<c>region-seg|region-head|region-subhead</c>），
    /// 三者互不嵌套、互不覆盖（均与 region-aside/main 平级或独立），使显式视图切换可回归区域计划（不再依赖整页刷新）；
    /// 刷新按钮 <c>data-poll-refresh</c> 留在区域外，避免被区域写覆盖丢失。</para>
    /// <para>P-A 浮层建模（I1/I6）：浮层为**独立可写区域** <c>region-overlay</c>（固定位置、与 region-main 平级），
    /// 使对话区重渲染不再回收浮层节点；插槽恒在（空内容即收起），壳稳定。</para></summary>
    public static string RenderShell(
        IReadOnlyDictionary<string, string> templates,
        string selectorHtml,
        string title,
        string subtitle,
        string treeHtml,
        string contentHtml,
        string? pollAttrs = null,
        string? overlayHtml = null,
        string? refreshTitle = null)
        => RenderShellWithMain(templates, selectorHtml, title, subtitle, treeHtml,
            MainRegion(contentHtml), pollAttrs, overlayHtml, refreshTitle);

    /// <summary>
    /// 分层布局壳（B3）：主区 <c>region-main</c> 降为**壳容器**，内含三个具名子容器
    /// <c>region-nodes</c>（树/面板/操作面）/ <c>region-bubbles</c>（气泡流，仅流段）/ <c>region-composer</c>（输入区）。
    /// 子容器即稳态写目标 ⇒ 消息变化只动气泡流，不重建主区。
    /// </summary>
    /// <param name="bubbles">气泡流段（已按 <see cref="WrapStreamSection"/> 包裹并带稳定段键，调用方与区域路径共用同一份）。</param>
    public static string RenderSplitShell(
        IReadOnlyDictionary<string, string> templates,
        string selectorHtml,
        string title,
        string subtitle,
        string treeHtml,
        string nodesHtml,
        IReadOnlyList<(string Key, string Html)> bubbles,
        string composerHtml,
        string? pollAttrs = null,
        string? overlayHtml = null,
        string? refreshTitle = null)
    {
        var bubbleHtml = new System.Text.StringBuilder();
        if (bubbles is { Count: > 0 })
        {
            foreach (var section in bubbles) bubbleHtml.Append(section.Html);
        }

        var main = "<div class=\"region\" data-name=\"" + RegionNamePrefix + "main\">"
                   + "<div class=\"region\" data-name=\"" + RegionNamePrefix + "nodes\">" + nodesHtml + "</div>"
                   + "<div class=\"region\" data-name=\"" + RegionNamePrefix + "bubbles\">" + bubbleHtml + "</div>"
                   + "<div class=\"region\" data-name=\"" + RegionNamePrefix + "composer\">" + composerHtml + "</div>"
                   + "</div>";
        return RenderShellWithMain(templates, selectorHtml, title, subtitle, treeHtml, main, pollAttrs, overlayHtml, refreshTitle);
    }

    /// <summary>气泡段包裹（单源）：段根带稳定 <c>data-key</c> ⇒ 流原语可按段键定点更新/追加。</summary>
    public static string WrapStreamSection(string key, string html)
        => "<div class=\"stream-item\" data-key=\"" + key + "\">" + html + "</div>";

    /// <summary>主区壳（单内容形态）。</summary>
    private static string MainRegion(string contentHtml)
        => "<div class=\"region\" data-name=\"" + RegionNamePrefix + "main\">" + contentHtml + "</div>";

    /// <summary>整页壳渲染核心（单一拼接点）：区域容器 + 模板投影。</summary>
    private static string RenderShellWithMain(
        IReadOnlyDictionary<string, string> templates,
        string selectorHtml,
        string title,
        string subtitle,
        string treeHtml,
        string mainRegionHtml,
        string? pollAttrs,
        string? overlayHtml,
        string? refreshTitle)
    {
        var treeRegion = "<div class=\"region\" data-name=\"" + RegionNamePrefix + "aside\">" + treeHtml + "</div>";
        // E-8：外壳动态位（选区/标题/副标题）各为独立区域元素（区域元素即 patch 目标，其内容 = 该位 HTML/文本）。
        var selectorRegion = "<div class=\"seg region\" data-name=\"" + RegionNamePrefix + "seg\">" + selectorHtml + "</div>";
        var titleRegion = "<span data-name=\"" + RegionNamePrefix + "head\">" + title + "</span>";
        var subtitleRegion = "<span data-name=\"" + RegionNamePrefix + "subhead\">" + subtitle + "</span>";
        // P-A：浮层独立区域（恒在；空内容 = 收起）。与 region-main 平级 ⇒ 对话区对齐不回收浮层。
        var overlayRegion = "<div class=\"region\" data-name=\"" + RegionNamePrefix + "overlay\">" + (overlayHtml ?? string.Empty) + "</div>";
        return HtmlTemplateEngine.Render(templates, "layoutShell",
            new Dictionary<string, object?>
            {
                ["selector"] = selectorRegion,
                ["title"] = titleRegion,
                ["subtitle"] = subtitleRegion,
                ["tree"] = treeRegion,
                ["history"] = mainRegionHtml,
                ["overlay"] = overlayRegion,
                ["pollAttrs"] = pollAttrs ?? string.Empty,
                ["refreshTitle"] = refreshTitle ?? string.Empty,
            });
    }
}
