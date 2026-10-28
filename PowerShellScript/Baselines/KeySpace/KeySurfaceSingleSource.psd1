@{
    # GP-1 键面单源基线（棘轮只降不升）：候选表 0 处、行键解析实现 1 处、前缀拼串 0 处。
    Schema = 'gate-baseline-v1'
    Gate   = 'KeySurfaceSingleSource'

    Value = @{ Value = @(
        @{ Key = 'candidate-row-keys'; Count = 0 }
        @{ Key = 'history-row-key-impl'; Count = 1 }
    ); Origin = 'Derived'; DueWhen = '' }

    # 存量债（只减不增）：Agent 域 `"history:" + ...` 的散落拼串；W1/D1 收敛到 0。
    Debt = @{ Value = @(
        @{ Key = 'prefix-concat'; Count = 3 }
    ); Origin = 'Debt'; DueWhen = 'W1/D1 键面收敛到 0' }
}
