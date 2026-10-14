#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 通用空态投影器 —— 统一「空态文案」的模板键与兜底资产，消除各渲染器各写一份空态片段。
/// 优先用 JSON 模板（<c>emptyDiv</c>/<c>emptyP</c>），缺失时回退 <see cref="HtmlAssetRegistry"/> 内嵌默认资产。
/// </summary>
public static class HtmlEmptyState
{
    /// <summary>块级空态模板名（JSON templates 键）。</summary>
    public const string DivTemplateName = "emptyDiv";

    /// <summary>段落空态模板名（JSON templates 键）。</summary>
    public const string ParagraphTemplateName = "emptyP";

    /// <summary>渲染块级空态（<c>&lt;div class="empty"&gt;</c>）：<paramref name="text"/> 经引擎 HTML 编码入 DOM。</summary>
    public static string Render(IReadOnlyDictionary<string, string> templates, string text)
        => RenderCore(templates, DivTemplateName, HtmlAssetKeys.EmptyDivTemplate, text);

    /// <summary>渲染段落空态（<c>&lt;p class="empty"&gt;</c>）：<paramref name="text"/> 经引擎 HTML 编码入 DOM。</summary>
    public static string RenderParagraph(IReadOnlyDictionary<string, string> templates, string text)
        => RenderCore(templates, ParagraphTemplateName, HtmlAssetKeys.EmptyPTemplate, text);

    private static string RenderCore(
        IReadOnlyDictionary<string, string> templates,
        string templateName,
        string assetKey,
        string text)
    {
        var model = new Dictionary<string, object?> { ["text"] = text };
        var asset = HtmlAssetRegistry.GetTemplate(assetKey);
        // 缺 JSON 模板时，用框架内嵌资产作 fallback（同样经引擎渲染，编码纪律一致）。
        var fallback = HtmlTemplateEngine.RenderTemplate(asset, null, model);
        return HtmlTemplateEngine.Render(templates, templateName, model, fallback);
    }
}
