@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-01-DeclarationNoImplRef'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-01-DeclarationNoImplRef.ps1'

    # 正对照下限：声明面扫描文件数不得收缩（派生自实测，不是债务）。
    ScanFiles = @{ Value = 55; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：只减不增。存量 = NodeConfig.cs 对 I 接口的 using 别名（B-5 标本）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Models/NodeConfig.cs' = 1
        }
        Origin = 'Debt'
        DueWhen = 'CW-01 依赖面归零（NodeConfig 去掉 INodeConfigProvider 别名，改为声明自有接口）'
    }
}
