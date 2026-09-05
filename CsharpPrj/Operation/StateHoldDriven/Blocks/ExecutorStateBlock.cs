
using BasicMethod.Block;
using ExecutionFramework.Engine;
using Operation.StateBlock.Abstractions;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    public class ExecutorStateBlock : CoreStateBlock
    {
        private readonly Func<Operation.StateBlock.Abstractions.IStateStore, FunctionExecutor>? _executorFactory;
        private readonly Func<Operation.StateBlock.Abstractions.IStateStore, PipelineExecutor>? _pipelineFactory;
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;

        public ExecutorStateBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Func<Operation.StateBlock.Abstractions.IStateStore, FunctionExecutor>? executorFactory = null,
            Func<Operation.StateBlock.Abstractions.IStateStore, PipelineExecutor>? pipelineFactory = null)
            : base("ExecutorStateBlock", store, "创建 FunctionExecutor 和 PipelineExecutor")
        {
            _opStore = store;
            _executorFactory = executorFactory;
            _pipelineFactory = pipelineFactory;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_executorFactory != null)
                {
                    var executor = _executorFactory(_opStore);
                    _opStore.Executor = executor;
                }

                if (_pipelineFactory != null)
                {
                    var pipeline = _pipelineFactory(_opStore);
                    _opStore.Pipeline = pipeline;
                }

                var executorStatus = _opStore.Executor != null ? "已创建" : "未创建（等待外部工厂）";
                var pipelineStatus = _opStore.Pipeline != null ? "已创建" : "未创建（等待外部工厂）";

                return BlockResult.Ok($"执行器状态块完成: Executor={executorStatus}, Pipeline={pipelineStatus}");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"执行器创建失败: {ex.Message}", ex);
            }
        }
    }
}
