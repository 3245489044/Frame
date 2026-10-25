#nullable enable

namespace UiBackend.Abstractions;

/// <summary>一次 wire 投递结果（Plate 侧只读事实；不含 Modules.Wiring 类型，避免层向污染）。</summary>
public readonly record struct UiWireDelivery(bool Delivered, string WireId, string? Reason = null, int Length = 0);

/// <summary>
/// 页面/宿主事件 → 接线框架的唯一入口（Plate 本地端口，原始类型）。
/// <para>由上层（Agent 组合根）适配到 <c>Modules.Wiring.Junction</c>；未装配 = 走旧直调路径（零回归）。</para>
/// </summary>
public interface IUiWireIngress
{
    ValueTask<UiWireDelivery> DeliverAsync(
        string wireId,
        string form,
        object? payload,
        string? traceId,
        CancellationToken cancellationToken = default);
}
