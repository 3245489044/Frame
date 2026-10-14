#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// UI 线程亲和端口（L0 控件适配层）——把「控件是否需封送 / 句柄是否已创建 / 如何封送」从具体控件抽离，
/// 使 <see cref="UiThreadMarshaler"/> 等组件可在无 WinForms 控件（测试替身）下复用与单测。
/// 由控件宿主（<c>Plate.HtmlViewer.HtmlViewer</c>）实现；<c>Control.InvokeRequired</c>/<c>IsHandleCreated</c> 隐式满足。
/// </summary>
public interface IUiThreadAffine
{
    /// <summary>调用线程是否非创建线程（需封送）。</summary>
    bool InvokeRequired { get; }

    /// <summary>控件句柄是否已创建（未创建时无法经 <c>InvokeAsync</c> 封送，回退 SynchronizationContext）。</summary>
    bool IsHandleCreated { get; }

    /// <summary>封送到 UI 线程执行（无返回值）。</summary>
    Task InvokeOnUiAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct);

    /// <summary>封送到 UI 线程执行（带返回值）。</summary>
    Task<T> InvokeOnUiAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct);
}
