@{
    # ===== UiBudget 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiBudget.ps1）=====
    # 语义：Append/Trim 动词四层闭合（契约 / 协调器 / 原语 / JS）。
    #   本件当前只锁「动词闭合」（B2 批判据）；帧预算阈值（ops/bytes/ms + degradeReason）随后续批扩项。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiBudget'

    VerbClosureLayers = @{
        Value = @(
            'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\UiWritePlan.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\DomOps.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs'
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
