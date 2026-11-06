@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-08-NoAmbientLookup'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-08-NoAmbientLookup.ps1'

    ScanFiles = @{ Value = 357; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧明确环境查找调用点（AppServiceProvider.Resolve / NodePortContractRegistry.Configure|Shared）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlLookup/EntityLookup.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Commands/LayoutInspectSystem.cs' = 6
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ControlStrategyRegistry.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutSubsystem.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/HostBasisSystem.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/TabContentReflowSystem.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlInteractionRoutePolicy.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/FunctionExecutedRenderBridge.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/PipelineCompositionFactory.cs' = 9
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs' = 5
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderValidationStage.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/InitCommandExecutor.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/ConfigLoadRenderService.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Ports.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Commands/ContainerCommandExecutor.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/StateServiceCollection.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/UiCatalogLibrary.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/UiCommandLibrary.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/WinForms/Container/HostMountService.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/KeySpace/Adapters/KeySetParse.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/KeySpace/Adapters/WorkflowKeySpaceDefaults.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs' = 1
        }
        Origin = 'Debt'
        DueWhen = 'CW-08 环境查找面归零（改为构造注入）后逐项销账'
    }
}
