@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-11-ConfigDefaultSingleSource'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-11-ConfigDefaultSingleSource.ps1'

    ScanFiles = @{ Value = 356; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧 `<配置接收段>.<字段> ?? <literal>` 配置缺省（第二真源）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ContractValidator.cs' = 2
        }
        Origin = 'Debt'
        DueWhen = 'CW-11 缺省值收归声明面模板/工厂后逐项销账'
    }
}

