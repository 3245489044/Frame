using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// HTML → 交互总线适配器（A2）：把 Plate 本地端口 <see cref="IHtmlInteractionPort"/> 适配到 ECS 事件端口
/// <see cref="IInteractionEventPort"/>（EcsEventBus）。
/// <para>语义：入队即分发（同步启动函数调用），随后等待真实完成信号（Completion）；
/// 重入分发返回 <see cref="EventDispatchOutcome.Dispatching"/> 且无 Completion ⇒ 如实报告未确认，不伪造成功。</para>
/// </summary>
public sealed class HtmlInteractionAdapter : IHtmlInteractionPort
{
    private readonly IInteractionEventPort _port;

    public HtmlInteractionAdapter(IInteractionEventPort port)
        => _port = port ?? throw new ArgumentNullException(nameof(port));

    public async Task<HtmlInteractionOutcome> DispatchAsync(
        string elementName,
        string eventName,
        string? payload,
        int? timeoutMs,
        CancellationToken ct)
    {
        EventDispatchResult result;
        try
        {
            result = _port.Enqueue(elementName, eventName, payload, timeoutMs);
        }
        catch (Exception ex)
        {
            return new HtmlInteractionOutcome(false, null, $"交互入队失败[{ex.GetType().Name}]: {ex.Message}");
        }

        if (result.Completion is null)
        {
            var dispatched = result.Outcome == EventDispatchOutcome.Dispatched;
            return new HtmlInteractionOutcome(dispatched, result.FunctionName,
                result.Error ?? (dispatched ? null : $"交互未分发: {result.Outcome}"));
        }

        try
        {
            var outcome = await result.Completion.WaitAsync(ct).ConfigureAwait(false);
            return new HtmlInteractionOutcome(true, result.FunctionName,
                outcome.Succeeded ? null : outcome.Error);
        }
        catch (OperationCanceledException)
        {
            result.Cancel?.Invoke();
            return new HtmlInteractionOutcome(false, result.FunctionName, "交互等待被取消");
        }
    }
}
