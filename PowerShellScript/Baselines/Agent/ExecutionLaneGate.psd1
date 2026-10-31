@{
    # ===== ExecutionLaneGate 门禁基线（单源；守卫 = Operation\DSL\Agent\Contract\Check-ExecutionLaneGate.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'ExecutionLaneGate'

    # 判据面下限：N1/N3 + N4(负对照) + N2(默认车道能力门)
    MinChecks = @{ Value = 3; Origin = 'Derived'; DueWhen = '判据增删时同步；只减不增须给理由' }
}
