@{
    # ===== NodeLocConsistency 门禁基线（单源；守卫 = Modules\Node\Check-NodeLocConsistency.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeLocConsistency'

    # 正对照：ReadScope 构造点数（须带 Loc）
    ReadScope = @{ Value = 4; Origin = 'Derived'; DueWhen = '可由 ReadScope 构造点扫描复算 ⇒ 后续改由生成器产出' }
}
