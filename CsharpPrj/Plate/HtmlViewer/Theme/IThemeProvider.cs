#nullable enable

namespace Plate.HtmlViewer;

/// <summary>主题作用域——Document=文档级（对话气泡/页面壳），Component=组件级（控件基础形态，对应原 BaseCss）。</summary>
public enum ThemeScope
{
    Document,
    Component,
}

/// <summary>
/// 主题提供者（全仓唯一主题真源）——按模式 + 作用域解析 <see cref="ThemeDocument"/>。
/// 消费方（<see cref="HtmlTheme"/>/<see cref="HtmlDocumentRenderer"/>/<see cref="HtmlViewer"/>）只依赖本抽象，
/// 具体来源由组合根注入（IoC 控制反转），消除「DefaultCss/theme/BaseCss/htmlShell 四份并存」。
/// </summary>
public interface IThemeProvider
{
    /// <summary>解析指定模式与作用域的主题文档；实现须非 null（无内容回退 <see cref="ThemeDocument.Empty"/>）。</summary>
    ThemeDocument Resolve(ThemeMode mode, ThemeScope scope);
}
