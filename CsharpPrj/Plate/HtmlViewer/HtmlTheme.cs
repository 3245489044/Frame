#nullable enable

using System;
using System.Threading;

namespace Plate.HtmlViewer;

/// <summary>
/// HTML 主题解析器（IoC 控制反转入口）——把「当前生效主题」的裁决反转给外部注入：
/// 优先级 = 宿主 <see cref="Attach"/> 装配的主题（如 AgentRuntimeUI.json templates.theme 的 ConfigHtmlTheme）&gt; 内嵌默认
/// <see cref="DefaultHtmlTheme"/>（包装 <see cref="HtmlDocumentRenderer.DefaultCss"/>）。组合根（宿主装配时）调用
/// <see cref="Attach"/> 覆盖；未注入回退默认，保证零回归。消费方（HtmlViewer/HtmlDocumentRenderer）只经 <see cref="Resolve"/> 读取。
/// </summary>
public static class HtmlTheme
{
    /// <summary>默认主题：包装 <see cref="HtmlDocumentRenderer.DefaultCss"/>（保留既有输出，作为未注入兜底）。</summary>
    public sealed class DefaultHtmlTheme : IHtmlTheme
    {
        /// <inheritdoc />
        public string Css => HtmlDocumentRenderer.DefaultCss;
    }

    /// <summary>内嵌默认主题单例（未注入时的回退）。</summary>
    public static readonly IHtmlTheme Default = new DefaultHtmlTheme();

    private static IHtmlTheme? _current;
    private static IThemeProvider? _provider;
    private static readonly IThemeProvider DefaultProvider = new DefaultThemeProvider();

    /// <summary>
    /// 当前生效主题（线程安全读取；唯一消费入口）。
    /// 优先级：已装配的 <see cref="IThemeProvider"/>（主题单一源，产出 <see cref="ThemeDocument"/>）&gt; 宿主
    /// <see cref="Attach"/> 装配的 <see cref="IHtmlTheme"/>（如 AgentRuntimeUI.json 的 ConfigHtmlTheme）&gt; 内嵌默认。
    /// </summary>
    public static IHtmlTheme Resolve()
    {
        var provider = Volatile.Read(ref _provider);
        if (provider is not null)
            return new ProviderTheme(provider.Resolve(ThemeMode.System, ThemeScope.Document));
        var current = Volatile.Read(ref _current);
        if (current is not null) return current;
        return Default;
    }

    /// <summary>当前生效主题提供者（线程安全读取；唯一真源）——已装配 provider 优先，否则回退内嵌默认（DefaultCss/BaseCss 按作用域产出）。</summary>
    public static IThemeProvider ResolveProvider() => Volatile.Read(ref _provider) ?? DefaultProvider;

    /// <summary>由宿主装配主题（幂等：覆盖式）。传入需非 null。</summary>
    public static void Attach(IHtmlTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Volatile.Write(ref _current, theme);
    }

    /// <summary>解除装配（回退内嵌默认）。</summary>
    public static void Detach() => Volatile.Write(ref _current, null);

    /// <summary>由组合根装配主题提供者（主题单一源；幂等：覆盖式，优先于 <see cref="Attach"/> 的 IHtmlTheme 装配）。</summary>
    public static void AttachProvider(IThemeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref _provider, provider);
    }

    /// <summary>解除主题提供者装配（回退既有 IHtmlTheme / 内嵌默认）。</summary>
    public static void DetachProvider() => Volatile.Write(ref _provider, null);

    /// <summary>把 <see cref="ThemeDocument"/> 适配为 <see cref="IHtmlTheme"/>（兼容消费端仍读 Css 字符串）。</summary>
    private sealed class ProviderTheme : IHtmlTheme
    {
        private readonly ThemeDocument _document;
        public ProviderTheme(ThemeDocument document) => _document = document;
        public string Css => _document.ToCss();
    }
}
