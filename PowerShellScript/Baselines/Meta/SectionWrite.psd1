@{
    # ===== SectionWrite 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-SectionWrite.ps1）=====
    # 语义：L1 写入口只接受 SectionPlan[]（节批量），Plate 内不存在「按 region 逐目标产出写目标」的第二条路径。
    Schema = 'gate-baseline-v1'
    Gate   = 'SectionWrite'

    SectionWriteContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\ISurfaceWriter.cs';
               Must = @('interface ISurfaceWriter', 'SubmitAsync') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.SurfaceWrite.cs';
               Must = @('ISurfaceWriter', 'SubmitAsync', 'WriteBudgetPolicy.Decide', '__uiReconcile.batch') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.PlanWrite.cs';
               Must = @('UseSectionWriter', 'SectionPlanAdapter.From') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\SectionPlanAdapter.cs';
               Must = @('class SectionPlanAdapter', 'SectionPlan') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
