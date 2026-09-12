
using ExecutionFramework.Engine;
using Operation.StateBlock.Abstractions;
using TConfig;
using WorkflowExecution.Pipeline;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 产品侧状态存储（OperationStateStore）—— 在 DataCenter 通用 <see cref="StateStore"/> 之上承载
    /// **服务定位器端口**（Registry / WorkflowRegistry / Executor / Pipeline）。
    /// 2026-09-12 DataCenter 立项（全量上收）：通用状态能力（cell 后端 / 字符串 KV / 对象 KV / StateChanged）
    /// 归 DataCenter.Core；产品专属端口留本类，使 DataCenter.Core 不引 ExecutionFramework/WorkFlowMethod（R-DC2）。
    /// </summary>
    public class OperationStateStore : StateStore, Operation.StateBlock.Abstractions.IStateStore
    {
        private readonly object _lock = new();
        private ITrConfigRegistry? _registry;
        private IWorkflowRegistry? _workflowRegistry;
        private FunctionExecutor? _executor;
        private PipelineExecutor? _pipeline;

        public OperationStateStore(TConfig.ContextFrame.Abstractions.IStorageCell cell,
            Share.StateManagement.IPublishDispatcher? stateChangedDispatcher = null)
            : base(cell, stateChangedDispatcher)
        {
        }

        public ITrConfigRegistry? Registry
        {
            get { lock (_lock) return _registry; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _registry = value;
            }
        }

        public IWorkflowRegistry? WorkflowRegistry
        {
            get { lock (_lock) return _workflowRegistry; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _workflowRegistry = value;
            }
        }

        public FunctionExecutor? Executor
        {
            get { lock (_lock) return _executor; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _executor = value;
            }
        }

        public PipelineExecutor? Pipeline
        {
            get { lock (_lock) return _pipeline; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _pipeline = value;
            }
        }

        public bool HasRegistry
        {
            get { lock (_lock) return _registry != null; }
        }

        public bool HasExecutor
        {
            get { lock (_lock) return _executor != null; }
        }

        public bool HasPipeline
        {
            get { lock (_lock) return _pipeline != null; }
        }

        public bool HasWorkflowRegistry
        {
            get { lock (_lock) return _workflowRegistry != null; }
        }

        /// <summary>清对象态 + 扩展端口（原 StateStore.ResetObjectState 语义）。</summary>
        public override void ResetObjectState()
        {
            ClearPorts();
            base.ResetObjectState();
        }

        /// <summary>清字符串 KV + 扩展端口（原 StateStore.Clear 语义）。</summary>
        public override void Clear()
        {
            ClearPorts();
            base.Clear();
        }

        private void ClearPorts()
        {
            lock (_lock)
            {
                _registry = null;
                _workflowRegistry = null;
                _executor = null;
                _pipeline = null;
            }
        }
    }
}
