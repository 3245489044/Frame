namespace Operation.ReconcileDriven.Host;

/// <summary>对账运行时选项。</summary>
public sealed class ReconcileOptions
{
    /// <summary>总开关：false ⇒ 全链直通（可审计的显式退出）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>终止信号等待窗（ms）：0 = 不等待（同步假设）。&gt;0 时供异步长任务在窗口内补齐终止信号。</summary>
    public int TerminalWaitBudgetMs { get; set; }
}
