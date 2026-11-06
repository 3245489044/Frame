@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-07-PlatformTypeQuarantine'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-07-PlatformTypeQuarantine.ps1'

    ScanFiles = @{ Value = 357; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：平台隔离边界之外的平台类型出现点（含 using 与 Control 类型引用）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EcsEventBus.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/InputCapturePort.cs' = 5
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlInteraction/State/EcsStateAccessor.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Commands/LayoutInspectSystem.cs' = 13
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ControlStaticDataApplier.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/ContainerBasisResolver.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutProjector.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutQuery.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/LayoutSubsystem.cs' = 9
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/HostBasisSystem.cs' = 5
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/LayoutSystem.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Systems/TabContentReflowSystem.cs' = 6
            'Frame/CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Gate/EcsOperation.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlViewerBridge/ObservedHtmlChannel.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/WinFormsUIDispatcher.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/ConfigLoadRenderService.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/Ports.cs' = 6
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ControlTypeRegistry.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/EventSubscriptionHelper.cs' = 15
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/Utils/PropertyApplier.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/Utils/SplitterMath.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/Utils/UiDispatcherCore.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/ScopeLifecycleAdapter.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/StateServiceCollection.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/UiCatalogLibrary.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/UiCommandLibrary.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Validation/Validators/StyleValidator.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/WorkflowMethodExecutionWorkflow.cs' = 2
        }
        Origin = 'Debt'
        DueWhen = 'CW-07 平台类型面归零（边界外改为端口抽象）后逐项销账'
    }
}
