@{
    # ===== DelegateEntry 门禁基线（单源；守卫 = Operation\DSL\Agent\Check-DelegateEntry.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DelegateEntry'

    # 判据面下限（防取数面漂移导致假绿）：D1(2) + D2(1) + D3(1) + D4(1)
    MinChecks = @{ Value = 5; Origin = 'Derived'; DueWhen = '判据增删时同步；只减不增须给理由' }
}
