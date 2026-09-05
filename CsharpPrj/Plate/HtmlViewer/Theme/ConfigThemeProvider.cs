#nullable enable

using System.Collections.Generic;

namespace Plate.HtmlViewer;

/// <summary>
/// 配置驱动主题提供者——接收已结构化的主题数据（token 浅/深 + 组件样式 + CSS 引用清单）产出 <see cref="ThemeDocument"/>。
/// 结构化数据由装配方（AgentRuntimeUI.json 编译层）解析后注入；替代「直接吃裸 CSS 字符串」的 ConfigHtmlTheme，
/// 使 JSON theme 从裸 CSS 升级为「token + 深浅色 + CSS 引用清单」的结构化声明。
/// </summary>
public sealed class ConfigThemeProvider : IThemeProvider
{
    private readonly ThemeDocument _document;

    public ConfigThemeProvider(ThemeDocument document)
        => _document = document ?? throw new System.ArgumentNullException(nameof(document));

    public ConfigThemeProvider(
        IReadOnlyDictionary<string, string>? tokensLight = null,
        IReadOnlyDictionary<string, string>? tokensDark = null,
        string? componentCss = null,
        IReadOnlyList<string>? cssFiles = null)
        : this(new ThemeDocument(tokensLight, tokensDark, componentCss, cssFiles))
    {
    }

    /// <summary>返回注入的主题文档（浅/深 token 已内含，@media 切深色，故不随 mode 变化）。</summary>
    public ThemeDocument Resolve(ThemeMode mode, ThemeScope scope) => _document;
}
