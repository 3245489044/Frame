@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-13-SnapshotOnly'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-13-SnapshotOnly.ps1'

    ScanFiles = @{ Value = 356; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：实现侧裸持有 MutableConfigType（未走 ConfigSnapshot<T>）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs' = 8
            'Frame/CsharpPrj/Operation/FrontEndDriven/Gate/EcsGate.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Gate/EcsOperation.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlBridgeAssembly.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlUiHost.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlUiHostAccess.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlUiHostComposition.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlHosting/HtmlUiHostSlot.cs' = 4
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/InitCommandExecutor.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/ConfigLoadRenderService.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/IRenderTargetRouter.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Ports.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/ProductionConsole/Console/ConsoleTemplateSet.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ConfigRole.cs' = 5
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ContractValidator.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/ControlConfigTarget.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/EventBindingFormatter.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/EventHelper.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/Shared/LoadedConfigInfo.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/BootHostBindingRegistrar.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/ScopeLifecycleAdapter.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Validation/RenderHostCycleGuard.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Adapters/CSG/CsgIntegrationService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/CallFrame/Abstractions/ICallFrameService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/CallFrame/Core/CallFrameService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Context/Runtime/WorkflowRuntimeContext.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ChainComposerFactory.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigDeduplicationProcessor.cs' = 8
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigRelationHandler.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/NodeChainMergeFacade.cs' = 6
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Services/NodeChainDocuments.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Services/NodeChainService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution/NodePortContractRegistry.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution/NodeResultExtractor.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/KeyProcessor.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeChainSkeleton.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeConfigFactory.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/NodeProcessPipelineBuilder.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Wiring/WorkflowWiringCatalogBuilder.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Orchestration/Validation.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/Abstractions/IParameterConfigProvider.cs' = 5
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterConfigProvider.cs' = 5
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/Builders/NodeIdentityGenerator.cs' = 4
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/Builders/WorkflowTools.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/Chain/WorkflowMediator.cs' = 9
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/Execution/Chain/WorkflowRegistrar.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/INodeExecutor.cs' = 4
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowExecution/INodeExecutorFactory.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs' = 1
        }
        Origin = 'Debt'
        DueWhen = 'CW-13 配置持有全部换成 ConfigSnapshot<T> 后逐项销账'
    }
}

