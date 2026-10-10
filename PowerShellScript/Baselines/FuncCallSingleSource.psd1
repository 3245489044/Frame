@{
    # ===== FuncCallSingleSource 门禁基线（单源；守卫 = Operation\DSL\Check-FuncCallSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'FuncCallSingleSource'

    # 正对照基线：FuncCallKind 成员数 ≥ 基线（分类被删而门禁恒绿 = 失效）
    MinKindMembers = @{ Value = 3; Origin = 'Derived'; DueWhen = '可由 FuncCallDispatch.cs 的 enum 成员数复算' }
}
