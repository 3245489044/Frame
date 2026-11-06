@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-04-NoConfigMutation'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-04-NoConfigMutation.ps1'

    ScanFiles = @{ Value = 356; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧对已持有配置的写入点（C 白名单为空）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigDeduplicationProcessor.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigRelationHandler.cs' = 5
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/KeyProcessor.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterConfigProvider.cs' = 2
        }
        Origin = 'Debt'
        DueWhen = 'CW-04 配置写归零（写回上移到 C 组合根）后逐项销账'
    }
}

