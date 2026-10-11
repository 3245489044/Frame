#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 通用页面布局壳投影器 —— 把「布局壳模板（flowShell）+ 切换器（selector）+ 浮层拼接（overlay）」的通用模板投影下沉到通用框架：
/// 纯模板投影 + 片段拼接，**零领域类型依赖**（不感知视图枚举 / 业务数据模型）。
/// 切换器项（key/label）与领域树/历史片段由宿主提供；布局结构由模板承担（改模板即改 UI，不改 C#）。
/// [下沉自 Agent.Launcher ConversationPageRenderer 的 Render/RenderSelector 通用部分，已通用化]
/// </summary>
public static class HtmlFlowProjector
{
    /// <summary>渲染切换器（viewSelector 模板）：<paramref name="items"/> 为 (键, 标签) 列表，当前项（= <paramref name="currentKey"/>）标 .active。</summary>
    public static string RenderSelector(
        IReadOnlyDictionary<string, string> templates,
        IReadOnlyList<(string Key, string Label)> items,
        string currentKey)
    {
        var rows = new List<object?>();
        foreach (var (key, label) in items)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["type"] = key,
                ["label"] = label,
                ["active"] = key == currentKey ? "1" : string.Empty,
            });
        }
        return HtmlTemplateEngine.Render(templates, "viewSelector",
            new Dictionary<string, object?> { ["items"] = rows });
    }

    /// <summary>把浮层（overlay）片段追加到主内容 HTML（position:fixed 悬浮于其上）；无浮层原样返回。</summary>
    public static string AppendOverlay(string contentHtml, string? overlayHtml)
        => contentHtml + (overlayHtml ?? string.Empty);

    /// <summary>
    /// 渲染「上下分栏」壳（<c>taskPaneShell</c> 模板）：上栏（<c>{nodes|raw}</c>）+ 下栏（<c>{history|raw}</c>）。
    /// 典型用途：观测面板（节点聚拢）叠于会话流之上；两栏片段由宿主已渲染好，本方法只做模板拼接。
    /// 缺模板 → 返回空串（调用方据此回退纯内容）。
    /// </summary>
    /// <param name="templates">模板集（含 <c>taskPaneShell</c> 键）。</param>
    /// <param name="primaryHtml">上栏 HTML 片段（如节点面板）。</param>
    /// <param name="secondaryHtml">下栏 HTML 片段（如会话历史 / 对话流）。</param>
    public static string RenderPaneSplit(
        IReadOnlyDictionary<string, string> templates,
        string primaryHtml,
        string secondaryHtml)
        => HtmlTemplateEngine.Render(templates, "taskPaneShell",
            new Dictionary<string, object?>
            {
                ["nodes"] = primaryHtml,
                ["history"] = secondaryHtml,
            });

    /// <summary>渲染布局壳（flowShell 模板）：左 tree + 右 content（含 overlay），顶部 selector。</summary>
    public static string RenderShell(
        IReadOnlyDictionary<string, string> templates,
        string selectorHtml,
        string title,
        string subtitle,
        string treeHtml,
        string contentHtml)
        => HtmlTemplateEngine.Render(templates, "flowShell",
            new Dictionary<string, object?>
            {
                ["selector"] = selectorHtml,
                ["title"] = title,
                ["subtitle"] = subtitle,
                ["tree"] = treeHtml,
                ["history"] = contentHtml,
            });
}
