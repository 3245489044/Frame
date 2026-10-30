@{
    Description = 'UI 中文字面量棘轮基线（只降不升）：扫描面 = UI 呈现链路；Declaration 层豁免（诊断/异常消息非 UI 文案，已登记）'
    ChineseBaseline = 29
    ScanRoots = @(
        'Projects\Plug\Agent\src\Agent.Ui\Ui'
    )
    ExemptPaths = @(
        'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration'
    )
    PendingRoots = @(
        'Projects\Plug\Agent\src\Agent.Session'
    )
}
