@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-09-NoStaticMutableState'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-09-NoStaticMutableState.ps1'

    ScanFiles = @{ Value = 357; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧静态可变字段/自动属性/静态单例引用。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutProjector.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlHostingLabels.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlInteractionRoutePolicy.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlUiHost.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/Registration/ControlServiceCollection.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/InitCommandGate.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ProductionConsole/Assistant/NullJsonProposalSource.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/Utils/EntityIdCache.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/Utils/PathManager.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/StateCenterFactory.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/StateServiceCollection.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/UiCommandLibrary.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/WinForms/Paint/SkinDecorator.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/CallFrame/Core/CallFrameService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Services/NodeChainDocuments.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution/NodePortContractRegistry.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/KeyHierarchyFormat.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/KeyProcessor.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeConfigFactory.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/FunctionExecutionFramework/DefaultWorkflowRegistry.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs' = 4
        }
        Origin = 'Debt'
        DueWhen = 'CW-09 静态可变状态归零（改为实例状态/构造注入）后逐项销账'
    }
}
