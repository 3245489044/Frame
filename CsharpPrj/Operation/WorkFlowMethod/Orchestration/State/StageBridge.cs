
#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using Share.StateManagement;
using Share.StateManagement.Stages;
using WorkflowExecution.Pipeline;
using WorkflowExecution.Abstractions;
using BasicExecutionResult = TConfig.ExecutionResult;
using WorkFlowMethod.Orchestration.State.Stages;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Orchestration.State
{
    public class StageBridge<TStage> : PipelineStageBase<WfPipelineContext>, WorkflowExecution.Abstractions.IPipelineStage
        where TStage : StageBase
    {
        private readonly TStage _innerStage;
        private readonly Func<WfPipelineContext, TStage, CancellationToken, Task>? _preProcess;
        private readonly Func<WfPipelineContext, TStage, CancellationToken, Task>? _postProcess;
        private readonly float _order;

        public StageBridge(
            TStage innerStage,
            string? name = null,
            string? description = null,
            Func<WfPipelineContext, TStage, CancellationToken, Task>? preProcess = null,
            Func<WfPipelineContext, TStage, CancellationToken, Task>? postProcess = null,
            float order = 0)
            : base(name ?? innerStage.Name, description ?? innerStage.Description)
        {
            _innerStage = innerStage ?? throw new ArgumentNullException(nameof(innerStage));
            _preProcess = preProcess;
            _postProcess = postProcess;
            _order = order;
        }

        public TStage InnerStage => _innerStage;

        float WorkflowExecution.Abstractions.IPipelineStage.Order => _order;

        protected override async Task ExecuteCoreAsync(
            WfPipelineContext context,
            CancellationToken cancellationToken)
        {
            if (_preProcess != null)
            {
                await _preProcess(context, _innerStage, cancellationToken);
            }

            await _innerStage.ExecuteAsync(context, cancellationToken);

            if (_postProcess != null)
            {
                await _postProcess(context, _innerStage, cancellationToken);
            }
        }


        async Task<BasicExecutionResult> WorkflowExecution.Abstractions.IPipelineStage.ExecuteAsync(
            TConfig.IExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (context is WfPipelineContext pipelineContext)
            {
                try
                {
                    await ExecuteCoreAsync(pipelineContext, cancellationToken);
                    return BasicExecutionResult.Success(null);
                }
                catch (OperationCanceledException)
                {
                    return BasicExecutionResult.Cancelled();
                }
                catch (Exception ex)
                {
                    return BasicExecutionResult.Failure(ex.Message);
                }
            }

            return BasicExecutionResult.Failure(
                $"Expected context type '{typeof(WfPipelineContext).Name}', but got '{context.GetType().Name}'");
        }
    }

    public static class StageBridgeFactory
    {

        public static StageBridge<InitStage> CreateInit(
            string name = "OrchestrationInit",
            string? description = null)
        {
            return new StageBridge<InitStage>(
                new InitStage(),
                name,
                description ?? "工作流初始化 - 委托给 Share.StateManagement.InitStage",
                order: 10);
        }

        public static StageBridge<PauseStage> CreatePause(
            IPauseManager? pauseManager = null,
            string name = "OrchestrationPause",
            string? description = null)
        {
            return new StageBridge<PauseStage>(
                new PauseStage(pauseManager),
                name,
                description ?? "工作流暂停/恢复控制 - 委托给 Share.StateManagement.PauseStage",
                order: 30);
        }

        public static StageBridge<ConfigStage> CreateConfig(
            IConfigProvider? configProvider = null,
            string name = "OrchestrationConfig",
            string? description = null)
        {
            return new StageBridge<ConfigStage>(
                new ConfigStage(configProvider),
                name,
                description ?? "配置刷新 - 委托给 Share.StateManagement.ConfigStage",
                order: 20);
        }

        public static StageBridge<ExecutionStage> CreateExecution(
            INodeExecutionCoordinator? coordinator = null,
            string name = "OrchestrationExecution",
            string? description = null)
        {
            return new StageBridge<ExecutionStage>(
                new ExecutionStage(coordinator, new Modules.StateMachine.StateMachineStore<Share.StateManagement.ExecutionStatus>(), new ExecutionParameterStore()),
                name,
                description ?? "执行状态管理 - 委托给 Share.StateManagement.ExecutionStage",
                order: 40);
        }

        public static StageBridge<LoopStage> CreateLoop(
            NodeProcessor? nodeProcessor = null,
            string name = "OrchestrationLoop",
            string? description = null)
        {
            return new StageBridge<LoopStage>(
                new LoopStage(nodeProcessor),
                name,
                description ?? "工作流循环执行 - 委托给 Share.StateManagement.LoopStage",
                order: 50);
        }

        public static StageBridge<RecordStage> CreateRecord(
            string name = "OrchestrationRecord",
            string? description = null)
        {
            return new StageBridge<RecordStage>(
                new RecordStage(),
                name,
                description ?? "执行记录汇总 - 委托给 Share.StateManagement.RecordStage",
                order: 60);
        }

        public static StageBridge<FinalizeStage> CreateFinalize(
            string name = "OrchestrationFinalize",
            string? description = null)
        {
            return new StageBridge<FinalizeStage>(
                new FinalizeStage(),
                name,
                description ?? "最终清理 - 委托给 Share.StateManagement.FinalizeStage",
                order: 70);
        }


        public static StageBase[] GetDefaultStages()
        {
            return new StageBase[]
            {
                new AdapterStage(new Modules.StateManagement.HandlerRegistry<string, Share.StateManagement.IWorkflowAdapter>()),
                new OrchestrationExecutionStage(),
                new OrchestrationLoopStage(),
                new OrchestrationErrorHandlingStage()
            };
        }

        public static StageBase[] GetFullStages()
        {
            return new StageBase[]
            {
                new InitStage(),
                new PauseStage(),
                new ConfigStage(),
                new AdapterStage(new Modules.StateManagement.HandlerRegistry<string, Share.StateManagement.IWorkflowAdapter>()),
                new OrchestrationExecutionStage(),
                new OrchestrationLoopStage(),
                new RecordStage(),
                new MetricsStage(),
                new OrchestrationErrorHandlingStage(),
                new FinalizeStage()
            };
        }
    }
}
