#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 渲染产物执行器（L1 通用契约）——写协调器对 <see cref="RenderOutput.Plan"/> 的单点执行入口。
/// 业务/渲染侧只产 <see cref="UiWritePlan"/>（<see cref="UiWriteKind"/> 类型的写回计划），
/// DOM 落地（区域/键控树/叶子/整页）一律由本执行器负责（单写者不变量）。
/// </summary>
public interface IUiRenderExecutor
{
    /// <summary>执行写回计划（唯一 DOM 写落地入口）；<paramref name="traceId"/> 为刷新链路追踪号。</summary>
    Task<UiWriteResult> ExecuteAsync(UiWritePlan plan, string? traceId = null, CancellationToken ct = default);
}
