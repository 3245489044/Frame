#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Modules.Observe;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 观察 HTML 通道 —— <see cref="IHtmlChannel"/> 的 Observe 收敛写扩展（装饰器契约）。
/// 纯通道（plate）只暴露渲染/封送面；Observe 收敛写（<see cref="WriteAsync"/>，返回 <see cref="ObserveOutcome"/>）
/// 定义在本连接层，由组合根把底层 htmlviewer 通道包装为 <see cref="IObservedHtmlChannel"/> 后供上层消费。
/// 依赖方向：上层(FrontEndDriven 连接层) → plate 抽象，倒转原 plate→Observe 污染。
/// </summary>
public interface IObservedHtmlChannel : IHtmlChannel
{
    /// <summary>
    /// 收敛写：投递 HTML 并等待「可观测」收敛——渲染完成 + 桥就绪（Converged）才视为成功；
    /// 界内未就绪返回 <see cref="ChangeResult.Degraded"/>（显式降级，绝不静默成功）。依 <see cref="WriteOptions.Barrier"/> 决定返回时机。
    /// </summary>
    Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default);
}
