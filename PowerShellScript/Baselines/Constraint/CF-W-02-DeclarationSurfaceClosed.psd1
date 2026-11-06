@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-02-DeclarationSurfaceClosed'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-02-DeclarationSurfaceClosed.ps1'

    ScanFiles = @{ Value = 55; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：只减不增。存量 = D 契约/模型中的 object/Delegate/平台类型等显式禁型。
    FileCounts = @{
        Value = @{
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Models/Dtos.cs' = 2
            'Frame/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/Models/NodeDefinition.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/EventBindingOptions.cs' = 3
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IControlFinderStrategy.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventBinder.cs' = 8
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventBindingService.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventBus.cs' = 11
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventExecutionService.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventHandlerFactory.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventMappingRegistry.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventTriggerResult.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IEventTriggerService.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IExecutionEngine.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IFunctionExecutor.cs' = 9
            'Frame/CsharpPrj/Plate/TConfig/Abstractions/IRenderPipeline.cs' = 13
            'Frame/CsharpPrj/Plate/TConfig/Models/ConfigModels.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Models/ControlConfig.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/ConversionOptions.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Models/EventBinding.cs' = 1
            'Frame/CsharpPrj/Plate/TConfig/Models/EventResult.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/ExecutionModels.cs' = 17
            'Frame/CsharpPrj/Plate/TConfig/Models/ExecutionPlan.cs' = 4
            'Frame/CsharpPrj/Plate/TConfig/Models/FunctionContext.cs' = 5
            'Frame/CsharpPrj/Plate/TConfig/Models/FunctionUnit.cs' = 9
            'Frame/CsharpPrj/Plate/TConfig/Models/IDataAccessor.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/IEventBindingResolver.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/ParamConfig.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/ParameterExecutionRequest.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/ParameterExecutionResult.cs' = 2
            'Frame/CsharpPrj/Plate/TConfig/Models/TrConfigRecord.cs' = 3
            'Frame/CsharpPrj/Plate/TConfig/Models/WorkflowDefinitionEntry.cs' = 6
        }
        Origin = 'Debt'
        DueWhen = 'CW-02 声明面闭合（object/Delegate/平台类型替换为强类型端口/DTO）后逐项销账'
    }
}
