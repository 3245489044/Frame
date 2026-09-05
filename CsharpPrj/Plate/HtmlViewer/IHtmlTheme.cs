#nullable enable

namespace Plate.HtmlViewer;

/// <summary>
/// HTML 文档主题——提供内嵌进 <c>&lt;head&gt;&lt;style&gt;</c> 的 CSS 片段。
/// 消费方（<see cref="HtmlDocumentRenderer"/> / <see cref="HtmlViewer"/>）只依赖本抽象，具体主题来源由外部注入（IoC 控制反转），
/// 不再硬编码一份主题。CSS 视为可信配置，直接内嵌、不做转义（与 htmlPage/templates 同信任级）。
/// </summary>
public interface IHtmlTheme
{
    /// <summary>主题 CSS 片段（直接内嵌 &lt;style&gt;，供 htmlviewer 包页壳时注入）。</summary>
    string Css { get; }
}
