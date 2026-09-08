namespace Strategy.Observability.Diagnostics;

/// <summary>
/// E8 诊断兜底：事件落库失败时经日志插槽触达（范本对齐 StallDetector / OutboxWriter 的 Action 插槽，
/// 内核零依赖）。默认落 Console；上层可注入 LogRepository / 自有日志门面。
/// </summary>
public sealed class ObservabilityFallback
{
    /// <param name="suffix">错误键后缀（如 DrainLoop_failed / AppendFailed）。</param>
    /// <param name="payload">可序列化载荷。</param>
    public delegate void Handler(string suffix, object? payload);

    public Handler? HandlerOverride { get; set; }

    public void Handle(string suffix, object? payload)
    {
        if (HandlerOverride is not null)
        {
            HandlerOverride(suffix, payload);
            return;
        }
        System.Console.WriteLine($"[obs:fallback:{suffix}] {payload}");
    }

    /// <summary>包装为可复用委托。</summary>
    public Action<string, object?> AsSlot() => Handle;
}
