@{
    # ===== UiSingleWrite 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiSingleWrite.ps1）=====
    # 语义：整文档原语 `SetHtmlAsync(` 的**允许调用面**（结构性白名单，非执行者叙述）。
    #   唯一允许 = 控件自身实现 + 「无协调器宿主」回退闭包；生产写入口一律 WriteContentAsync / UiWritePlan。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiSingleWrite'

    AllowedCallSites = @{
        Value = @(
            'Frame\CsharpPrj\Plate\HtmlViewer\HtmlViewer.cs'
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
