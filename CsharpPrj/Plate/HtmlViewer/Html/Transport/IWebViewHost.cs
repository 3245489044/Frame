#nullable enable

using System;

namespace UiBackend.Html;

/// <summary>
/// WebView2 控件宿主**完整端口**（L0 控件适配层）——在 <see cref="IWebViewCore"/> 之上追加交互面
/// （UI 线程投递 / 真实输入前置激活 / 宿主窗体句柄），供消息传输 / 回流探针 / 真实输入等组件消费。
/// 由控件宿主（<c>Plate.HtmlViewer.HtmlViewer</c>）实现。
/// </summary>
public interface IWebViewHost : IWebViewCore
{
    /// <summary>把动作投递回 UI 线程（句柄未创建 / 已释放时同步执行）——消息回调封送用（BeginInvoke 语义）。</summary>
    void Post(Action action);

    /// <summary>激活宿主窗体并聚焦控件（真实输入前置条件；无窗体时为空操作）。</summary>
    void ActivateForInput();

    /// <summary>宿主窗体句柄（无窗体 / 无句柄为 <see cref="IntPtr.Zero"/>）。</summary>
    IntPtr HostFormHandle { get; }
}
