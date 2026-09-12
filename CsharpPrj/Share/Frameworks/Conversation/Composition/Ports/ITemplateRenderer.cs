using System.Collections.Generic;

namespace Share.Composition.Ports;

/// <summary>
/// 模板渲染端口 —— 内核不绑定任何具体模板引擎（HTML 引擎驻留 Plate 层，内核不得反向引用）。
/// 端口签名与 <c>UiBackend.Html.HtmlTemplateEngine</c> 的公开面一一对应，使适配器成为纯 1:1 委托
/// （等价性可直接由测试证明，零语义转换风险）。
/// 实现方：<c>FrontEndDriven.HtmlViewerBridge.HtmlTemplateRenderer</c>（委托 HtmlTemplateEngine）。
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// 渲染命名模板（在 <paramref name="templates"/> 内按 <paramref name="name"/> 查找；缺失/空 → <paramref name="fallback"/>）。
    /// </summary>
    /// <param name="templates">模板集（命名模板 + 供 <c>{&gt;name}</c> 子模板调用解析）。</param>
    /// <param name="name">模板名。</param>
    /// <param name="model">行数据模型（键大小写不敏感；null → 空模型）。</param>
    /// <param name="fallback">模板缺失时的兜底输出。</param>
    string Render(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?>? model,
        string fallback = "");

    /// <summary>渲染一段模板字符串（<paramref name="templates"/> 供 <c>{&gt;name}</c> 调用解析，可 null）。</summary>
    /// <param name="template">模板串（null/空 → 空串）。</param>
    /// <param name="templates">命名模板集（可 null）。</param>
    /// <param name="model">行数据模型（null → 空模型）。</param>
    string RenderTemplate(
        string? template,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?>? model);
}
