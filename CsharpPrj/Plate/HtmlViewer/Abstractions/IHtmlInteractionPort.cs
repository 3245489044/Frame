namespace UiBackend.Abstractions;

/// <summary>
/// HTML 交互路由（A2 灰度开关）：<see cref="Legacy"/> = 事件桥直调注册函数（旧路径）；
/// <see cref="Bus"/> = 经交互端口入 ECS 事件总线（InvocationKernel → FunctionHub → 工作流）。
/// </summary>
public enum HtmlInteractionRoute
{
    Legacy,
    Bus
}

/// <summary>交互端口分发结果（Plate 本地契约，零上层依赖；由 FrontEndDriven 适配 IInteractionEventPort）。</summary>
public sealed record HtmlInteractionOutcome(
    bool Dispatched,
    string? FunctionName = null,
    string? Error = null);

/// <summary>
/// HTML → 交互总线的本地端口（A2）：页面事件按「域限定元素名 + 事件名 + 负载」投递；
/// 写回由总线侧（计划执行器）负责，事件桥不二次写。
/// </summary>
public interface IHtmlInteractionPort
{
    Task<HtmlInteractionOutcome> DispatchAsync(
        string elementName,
        string eventName,
        string? payload,
        int? timeoutMs,
        CancellationToken ct);
}
