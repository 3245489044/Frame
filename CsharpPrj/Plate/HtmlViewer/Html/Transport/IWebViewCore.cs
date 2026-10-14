#nullable enable

using System;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace UiBackend.Html;

/// <summary>
/// WebView2 控件宿主**核心端口**（L0 控件适配层）——生命周期宿主所需的最小面：
/// CoreWebView2 访问 + 一次性初始化 + 导航 + 默认背景色锚定 + 句柄/释放态 + UI 线程亲和。
/// 通用查看器（如 <c>Plate.MarkdownViewer.MarkdownViewer</c>）只需实现本端口即可复用 <see cref="WebViewLifecycleHost"/>；
/// <c>InvokeRequired</c>/<c>IsHandleCreated</c>/<c>Handle</c>/<c>IsDisposed</c> 由控件隐式满足。
/// </summary>
public interface IWebViewCore : IUiThreadAffine
{
    /// <summary>当前 CoreWebView2（未初始化 / 被释放为 null）。</summary>
    CoreWebView2? Core { get; }

    /// <summary>控件是否已释放。</summary>
    bool IsDisposed { get; }

    /// <summary>控件句柄（未创建为 <see cref="IntPtr.Zero"/>）。</summary>
    IntPtr Handle { get; }

    /// <summary>确保 CoreWebView2 完成初始化（仅限 UI 线程）。</summary>
    Task EnsureCoreAsync();

    /// <summary>以完整 HTML 文档导航当前视图（<c>NavigateToString</c> 语义）。</summary>
    void NavigateToHtmlDocument(string html);

    /// <summary>WebView2 默认背景色（主题背景锚定）。</summary>
    Color DefaultBackgroundColor { get; set; }
}
