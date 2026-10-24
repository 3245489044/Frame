@{
    # ===== UiIncremental 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiIncremental.ps1）=====
    # 语义：append-only 面的「增量契约载体」与「整页渲染允许面」。
    #   sink 必须同时具备 UiWritePlan.Append( 与 RenderBlock( ，且不得回退 SetHtmlAsync(。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiIncremental'

    SinkFile = @{
        Value = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\LogHtmlViewSink.cs'
        Origin = 'Derived'
        DueWhen = ''
    }

    RendererFile = @{
        Value = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\LogHtmlFoldRenderer.cs'
        Origin = 'Derived'
        DueWhen = ''
    }
}
