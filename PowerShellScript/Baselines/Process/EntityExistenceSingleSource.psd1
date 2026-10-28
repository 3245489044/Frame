@{
    # GP-2 实体存在性单源基线（棘轮只降不升）：直读探针 0、实现 2（未装配桩 + 注册表）、重复前置检查 0、未映射可见 1。
    Schema = 'gate-baseline-v1'
    Gate   = 'EntityExistenceSingleSource'

    Value = @{ Value = @(
        @{ Key = 'direct-probe'; Count = 0 }
        @{ Key = 'existence-impl'; Count = 2 }
        @{ Key = 'duplicate-precheck'; Count = 0 }
        @{ Key = 'unmapped-visible'; Count = 1 }
    ); Origin = 'Derived'; DueWhen = '' }
}
