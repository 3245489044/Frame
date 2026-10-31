@{
    # ===== PromptSegments 门禁基线（单源；守卫 = Operation\DSL\Agent\Declaration\Check-PromptSegments.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'PromptSegments'

    # 判据面下限：P1 + P2 + P3(两条会话链) = 4
    MinChecks = @{ Value = 4; Origin = 'Derived'; DueWhen = '判据增删时同步；只减不增须给理由' }
}
