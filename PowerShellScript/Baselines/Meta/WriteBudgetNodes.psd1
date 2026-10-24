@{
    # ===== WriteBudgetNodes 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-WriteBudgetNodes.ps1）=====
    # 语义：预算以**节点数**为主（D4）；选项声明 MaxNodesPerBeat，策略与写入口真实消费它。
    Schema = 'gate-baseline-v1'
    Gate   = 'WriteBudgetNodes'

    BudgetContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\UiWriteRuntimeOptions.cs';
               Must = @('MaxNodesPerBeat', 'MaxNodesPerBeatBackground') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\WriteBudgetPolicy.cs';
               Must = @('MaxNodes', 'SectionVariance.Volatile', 'oversize-single-section') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.SurfaceWrite.cs';
               Must = @('MaxNodesPerBeat', 'WriteBudgetPolicy.Decide', 'OnDropped') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
