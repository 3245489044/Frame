
using TConfig;
using ExecutionFramework.Engine;
using WorkFlowMethod.Orchestration.Context.Models;

namespace WorkFlowMethod.FunctionExecutionFramework.Core
{
    public class DefaultWorkflowRegistry : IWorkflowRegistry
    {
        private readonly ITrConfigRegistry _innerRegistry;

        public static DefaultWorkflowRegistry Default { get; } = new DefaultWorkflowRegistry();

        public DefaultWorkflowRegistry(ITrConfigRegistry? innerRegistry = null)
        {
            _innerRegistry = innerRegistry ?? new TrConfigRegistry();
        }


        public void Register(string name, ITrConfig config) => _innerRegistry.Register(name, config);
        public void Register(string name, Delegate function, string? description = null) => _innerRegistry.Register(name, function, description);
        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null) => _innerRegistry.Register(name, function, parameters, description);
        public void Unregister(string name) => _innerRegistry.Unregister(name);
        public bool TryGetConfig(string name, out ITrConfig config) => _innerRegistry.TryGetConfig(name, out config);
        public bool Contains(string name) => _innerRegistry.Contains(name);
        public IEnumerable<string> GetRegisteredNames() => _innerRegistry.GetRegisteredNames();
        public void Clear() => _innerRegistry.Clear();
        public Delegate? Find(string functionName) => _innerRegistry.Find(functionName);
        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor) => _innerRegistry.TryGetDescriptor(name, out descriptor);
        public bool TryGetDelegate(string name, out Delegate? function) => _innerRegistry.TryGetDelegate(name, out function);


        public object? Call(string name, params object?[] args)
        {
            if (!_innerRegistry.TryGetConfig(name, out var config))
                throw new FunctionNotFoundException(name);
            return config.Invoke(args);
        }

        public async Task<object?> CallAsync(string name, params object?[] args)
        {
            if (!_innerRegistry.TryGetConfig(name, out var config))
                throw new FunctionNotFoundException(name);
            // Task 解包唯一权威：TaskHelper（声明返回类型优先，兼容 async lambda 状态机盒，
            // 见 TaskHelper.InvokeAndUnwrapAsync 语义）。此处不再自行做运行时 GetType() 判定。
            return await TaskHelper.InvokeAndUnwrapAsync(config, args);
        }

        public async Task<object?> CallWithContextAsync(string name, FunctionContext context, params object?[] args)
        {
            var allArgs = new List<object?> { context };
            allArgs.AddRange(args);
            return await CallAsync(name, allArgs.ToArray());
        }

        public bool HasFunction(string name) => _innerRegistry.Contains(name);

        public IEnumerable<string> GetFunctionNames() => _innerRegistry.GetRegisteredNames();


        public void RegisterWorkflow(string name, Func<WorkflowContext, object> func)
        {
            ArgumentNullException.ThrowIfNull(func);
            Register(name, (Delegate)((FunctionContext ctx) => func(ToWorkflowContext(ctx))));
        }

        public void RegisterWorkflowAsync(string name, Func<WorkflowContext, Task<object>> func)
        {
            ArgumentNullException.ThrowIfNull(func);
            // ConfigureAwait(false)：与 RegisterAsyncFunction 同理，库回调不应捕获环境 SynchronizationContext，
            // 避免在带 SyncContext 线程被调用时续延编组回阻塞上下文造成异步死锁。
            Register(name, (Delegate)(async (FunctionContext ctx) => await func(ToWorkflowContext(ctx)).ConfigureAwait(false)));
        }

        public object CallWorkflow(string name, WorkflowContext context)
            => Call(name, context)!;

        public async Task<object> CallWorkflowAsync(string name, WorkflowContext context)
            => (await CallAsync(name, context))!;

        private static WorkflowContext ToWorkflowContext(FunctionContext ctx)
        {
            return new WorkflowContext(
                parameters: ctx.Parameters!,
                data: ctx.Items,
                executionId: ctx.ExecutionId,
                nodeKey: null,
                workflowId: null
            );
        }
    }
}
