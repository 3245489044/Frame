using TConfig;

namespace Operation.ReconcileDriven.Contracts;

/// <summary>
/// 规范化后的对账契约（<see cref="ReconcileConfig"/> 的运行时视图）：补齐缺省、剔空项。
/// <para>缺省口径：<c>mode</c> 缺省 <see cref="ReconcileModes.Off"/>（不对账，零回归）；<c>order</c> 缺省 <see cref="ReconcileOrders.Strict"/>。</para>
/// </summary>
public sealed record ReconcileContract(string Mode, string Order, IReadOnlyList<SignalExpectation> Signals)
{
    /// <summary>是否显式退出对账。</summary>
    public bool IsOff => string.Equals(Mode, ReconcileModes.Off, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否严格模式（失配结构化抛错）。</summary>
    public bool IsStrict => string.Equals(Mode, ReconcileModes.Strict, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否告警模式（失配仅落事件，不抛）。</summary>
    public bool IsWarn => string.Equals(Mode, ReconcileModes.Warn, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否严格顺序（须按声明序成链）。</summary>
    public bool IsStrictOrder => string.Equals(Order, ReconcileOrders.Strict, StringComparison.OrdinalIgnoreCase);

    /// <summary>从声明面模型规范化；<paramref name="cfg"/> 为 null → 不对账（返回 null）。</summary>
    public static ReconcileContract? From(ReconcileConfig? cfg)
    {
        if (cfg is null) return null;

        var mode = string.IsNullOrWhiteSpace(cfg.Mode) ? ReconcileModes.Off : cfg.Mode!.Trim();
        var order = string.IsNullOrWhiteSpace(cfg.Order) ? ReconcileOrders.Strict : cfg.Order!.Trim();
        var signals = cfg.Signals?
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Signal))
            .ToList() ?? new List<SignalExpectation>();

        return new ReconcileContract(mode, order, signals);
    }
}
