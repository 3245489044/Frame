using Microsoft.Web.WebView2.Core;

namespace Plate.HtmlViewer;

/// <summary>HTML 主题模式——深浅色显式锚定；System 才随系统 prefers-color-scheme。</summary>
public enum ThemeMode
{
    Light,
    Dark,
    System
}

/// <summary>ThemeMode → WebView2 配色枚举映射（唯一锚定点，避免各处 switch 漂移）。</summary>
public static class ThemeModeExtensions
{
    public static CoreWebView2PreferredColorScheme Map(this ThemeMode mode)
        => mode switch
        {
            ThemeMode.Light => CoreWebView2PreferredColorScheme.Light,
            ThemeMode.Dark => CoreWebView2PreferredColorScheme.Dark,
            _ => CoreWebView2PreferredColorScheme.Auto,
        };
}
