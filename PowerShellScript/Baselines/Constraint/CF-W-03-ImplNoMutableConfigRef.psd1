@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-03-ImplNoMutableConfigRef'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-03-ImplNoMutableConfigRef.ps1'

    ScanFiles = @{ Value = 97; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：核心目录现存的可变配置类型声明（字段/参数/局部/属性）。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs' = 8
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineStages.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderStageRunner.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/ConfigurationLifecycleManager.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/Lifecycle/InitCommandExecutor.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/ConfigLoadRenderService.cs' = 1
            'Frame/CsharpPrj/Operation/FrontEndDriven/LoadRender/IRenderTargetRouter.cs' = 3
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/BootHostBindingRegistrar.cs' = 2
            'Frame/CsharpPrj/Operation/FrontEndDriven/State/Projection/ScopeLifecycleAdapter.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Context/Runtime/WorkflowRuntimeContext.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ChainComposerFactory.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigDeduplicationProcessor.cs' = 8
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/ConfigRelationHandler.cs' = 3
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Deduplication/NodeChainMergeFacade.cs' = 6
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Services/NodeChainDocuments.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/GraphUsing/Services/NodeChainService.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution/NodePortContractRegistry.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Execution/NodeResultExtractor.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Orchestration/Validation.cs' = 1
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/Abstractions/IParameterConfigProvider.cs' = 5
            'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterConfigProvider.cs' = 5
        }
        Origin = 'Debt'
        DueWhen = 'CW-03 核心面归零（改为 ConfigSnapshot/值对象读取）后逐项销账'
    }
}
