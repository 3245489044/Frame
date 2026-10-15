@{
    # ===== VerifyScope 注册表（非单件门禁基线；`_` 前缀 ⇒ 不进入 GateBaselines 单件判据）=====
    # 语义：验证「局部面」免授权直跑；「全量面」默认禁止，须持授权（用户裁决 2026-10-15）。
    #   消费方 = Environment\CentralTest\Internal\Invoke-Verify.ps1（P2 唯一验证命令）
    #          ∧ Environment\CentralTest\Internal\Invoke-AgentRegression.ps1（矩阵执行体）
    #   裁决原语单源 = Baselines\VerifyScope.psm1（禁各消费方手写解析）
    #   守卫 = Operation\DSL\Check-VerifySingleShot.ps1 判据 ④（接线：注册表 ∧ 两执行体裁决标记）
    Schema = 'verify-scope-v1'

    # 默认禁止全量：true = 未持授权时全量面 fail-closed（禁静默放大为全面测试）
    FullRunRequired = @{ Value = $true; Origin = 'Verdict'; DueWhen = '全量面授权机制稳定后按实测重裁' }

    # 全量面枚举（结构事实；新增放大面须先在此登记 ⇒ 未登记面不得被当作局部面放行）
    FullScopeKinds = @(
        @{ Kind = 'matrix';   Reason = '测试矩阵（Invoke-AgentRegression -Matrix <default|agent|infra>）' }
        @{ Kind = 'fullscan'; Reason = '无目标全量扫描（Run-CSharpTests 未指定目标 / -ThenFull）' }
        @{ Kind = 'gates';    Reason = '全部门禁（Invoke-GateCheck -All）' }
    )

    # GatesAll 未授权时的降级面（聚合器自检件）；其余覆盖面须 report 可见化（禁静默丢面）
    SelfCheckGates = @(
        'InvokeEntrypoints'
        'VerifySingleShot'
        'GateBaselines'
        'BatchScope'
        'VerifyProportionality'
    )

    # 宿主授权令牌通道（由宿主进程注入；文件面仅声明通道名，禁执行者自授权）
    GrantTokenEnvVar = @{ Value = 'WF_VERIFY_FULL_GRANT'; Origin = 'Derived'; DueWhen = '' }

    # 批次授权单中承载全量授权的字段名（machine-readable；缺字段 = 未授权）
    GrantField = @{ Value = 'verify'; Origin = 'Derived'; DueWhen = '' }
}
