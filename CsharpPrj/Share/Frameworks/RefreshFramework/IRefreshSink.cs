using System.Threading;
using System.Threading.Tasks;

namespace Share.RefreshFramework
{
    /// <summary>
    /// 写落地端口（L0 契约，通用）：宿主实现，包裹具体执行器（如 DOM 写 / 通道投递）。
    /// <para>Share 框架不引用任何上层 —— 由宿主适配器反向注入实现，保持下层"零上层依赖"。</para>
    /// </summary>
    public interface IRefreshSink
    {
        /// <summary>落地一次交付。</summary>
        /// <returns>true = 已落地；false = 未送达（调用方置回重试，at-least-once）。</returns>
        Task<bool> DeliverAsync(RefreshDelivery delivery, CancellationToken ct = default);
    }
}
