@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-05-NoConfigConstruction'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-05-NoConfigConstruction.ps1'

    ScanFiles = @{ Value = 357; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧现存显式配置构造/反序列化点。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/ProductionConsole/Console/ConsoleTemplateSet.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigDeduplicationProcessor.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Directives/GraphUpdateDirective.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/Builders/WorkflowTools.cs' = 1
        }
        Origin = 'Debt'
        DueWhen = 'CW-05 构造面归零（构造上移到加载器/工厂，实现侧只读快照）后逐项销账'
    }
}
