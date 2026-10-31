@{
    # ===== LaneCatalog 门禁基线（单源；守卫 = Operation\DSL\Agent\Declaration\Check-LaneCatalog.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'LaneCatalog'

    # 判据面下限（防取数面漂移导致假绿）：L1(1) + L2/L3/L4(1) + L4/L5(2)
    MinChecks = @{ Value = 4; Origin = 'Derived'; DueWhen = '判据增删时同步；只减不增须给理由' }
}
