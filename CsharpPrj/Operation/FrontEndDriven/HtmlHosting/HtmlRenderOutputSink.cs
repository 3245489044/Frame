#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// HTML 写落地端口（通用实现）—— 把刷新产出的 <see cref="RenderOutput"/> 交付给 DOM 写协调器。
/// <para>未装配执行器 / 产物非写计划 ⇒ 未送达（不推进 baseline，可重试）；
/// Skipped/Nothing ⇒ 已送达但未变化；Applied/Partial ⇒ 已送达且内容变化（并可回调「直接写已落地」）。</para>
/// </summary>
public sealed class HtmlRenderOutputSink : IRefreshSink
{
    private readonly Func<IUiRenderExecutor?> _executor;
    private readonly Action? _onDelivered;

    /// <param name="executor">执行器解析（缺省 = 默认 HTML 宿主槽的运行时；返回 null = 未装配）。</param>
    /// <param name="onDelivered">直接写落地回调（用于消除「写已落地仍 push 重算」的回环）。</param>
    public HtmlRenderOutputSink(Func<IUiRenderExecutor?>? executor = null, Action? onDelivered = null)
    {
        _executor = executor ?? (static () => HtmlUiHost.Default.HtmlUi);
        _onDelivered = onDelivered;
    }

    /// <inheritdoc />
    public async Task<DeliveryResult> DeliverAsync(RefreshDelivery delivery, CancellationToken ct = default)
    {
        if (delivery.Payload is not RenderOutput output || output.Plan is not { } plan)
            return DeliveryResult.NotDelivered();

        var executor = _executor();
        if (executor is null) return DeliveryResult.NotDelivered();

        try
        {
            var result = await executor.ExecuteAsync(plan, delivery.TraceId, ct).ConfigureAwait(false);
            if (result.Status is UiWriteStatus.Applied or UiWriteStatus.Partial)
            {
                _onDelivered?.Invoke();
                return DeliveryResult.Changed(delivery.Fingerprint);
            }

            return result.Status switch
            {
                UiWriteStatus.Skipped or UiWriteStatus.Nothing => DeliveryResult.Unchanged(delivery.Fingerprint),
                _ => DeliveryResult.NotDelivered(),
            };
        }
        catch (Exception ex)
        {
            return DeliveryResult.NotDelivered(ex);
        }
    }
}
