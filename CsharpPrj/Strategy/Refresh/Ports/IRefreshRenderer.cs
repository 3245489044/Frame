using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 刷新请求（框架 → 域）：一次刷新的完整上下文（范围 / 受控态 / 追踪号）。
    /// </summary>
    /// <param name="SlotKey">目标槽键（渲染器身份）。</param>
    /// <param name="Scopes">范围集合（null / 空 = 显式全量）。</param>
    /// <param name="ContextJson">页面回投上下文原串（域自解析；框架不解释）。</param>
    /// <param name="TraceId">追踪号（一次刷新一条轨迹；可为 null）。</param>
    /// <param name="SelectionKeys">选择态线格式（框架态外置协议 <c>uiState.select</c>，<c>"slot=key"</c>；null = 未携带）。</param>
    /// <param name="OpenKeys">受控展开集合（框架态外置协议 <c>uiState.open</c>；null = 未携带）。</param>
    public sealed record RefreshRequest(
        string SlotKey,
        IReadOnlyList<string>? Scopes = null,
        string? ContextJson = null,
        string? TraceId = null,
        IReadOnlyList<string>? SelectionKeys = null,
        IReadOnlyList<string>? OpenKeys = null);

    /// <summary>
    /// 领域渲染端口（L1 **唯一接入点**）：从「做什么」到「长什么样」的生产者。
    /// <para>必须返回带 <see cref="RefreshOutcome"/> / <see cref="RefreshArtifact.Fingerprint"/> / 进度令牌的
    /// <see cref="RefreshArtifact"/>；框架不再接受不透明 object 产物。</para>
    /// </summary>
    public interface IRefreshRenderer
    {
        /// <summary>槽键（框架内唯一；重复注册须替换或报错）。</summary>
        string SlotKey { get; }

        /// <summary>产出一次带结论的渲染产物。</summary>
        Task<RefreshArtifact> RenderAsync(RefreshRequest request, CancellationToken ct = default);
    }
}
