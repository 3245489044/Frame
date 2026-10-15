using System.Threading;
using System.Threading.Tasks;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 刷新请求（框架 → 域）：一次刷新的完整上下文（范围 / 受控态 / 追踪号）。
    /// </summary>
    /// <param name="SlotKey">目标槽键（渲染器身份）。</param>
    /// <param name="Scopes">范围集合（null / 空 = 显式全量）。</param>
    /// <param name="ContextJson">页面回投上下文原串（域自解析；框架不解释）。</param>
    /// <param name="TraceId">追踪号（一次刷新一条轨迹；可为 null）。</param>
    public sealed record RefreshRequest(
        string SlotKey,
        IReadOnlyList<string>? Scopes = null,
        string? ContextJson = null,
        string? TraceId = null);

    /// <summary>
    /// 领域渲染端口（L1 **唯一接入点**）：从「做什么」到「长什么样」的生产者。
    /// <para>框架只负责「何时刷 / 是否刷 / 怎么落地」，不解释渲染产物类型（object 出口）。</para>
    /// </summary>
    public interface IRefreshRenderer
    {
        /// <summary>槽键（框架内唯一；重复注册须替换或报错）。</summary>
        string SlotKey { get; }

        /// <summary>产出一次渲染（产物类型由域定义，框架透传）。</summary>
        Task<object> RenderAsync(RefreshRequest request, CancellationToken ct = default);
    }
}
