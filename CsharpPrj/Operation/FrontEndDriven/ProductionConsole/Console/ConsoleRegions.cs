namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>控制台区域键（与面板 htmlPage.regions 声明一一对应；写回按 region:&lt;key&gt; 收敛）。</summary>
public static class ConsoleRegions
{
    public const string Caps = "region-caps";
    public const string Form = "region-form";

    /// <summary>AI 参数助理独立区域（与表单解耦：解析只刷新本区，不动表单 DOM）。</summary>
    public const string Assist = "region-assist";

    public const string Jobs = "region-jobs";
    public const string Result = "region-result";
    public const string Drawer = "region-drawer";
    public const string Status = "region-status";

    public static readonly string[] All = { Caps, Form, Assist, Jobs, Result, Drawer, Status };
}
