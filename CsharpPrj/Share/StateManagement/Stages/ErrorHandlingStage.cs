
using Share.StateManagement;
using Modules.StateManagement;

namespace Share.StateManagement.Stages
{
    public class ErrorHandlingStage : StageBase
    {
        private readonly IHandlerRegistry<string, IErrorHandler> _handlerRegistry;

        public ErrorHandlingStage(
            IHandlerRegistry<string, IErrorHandler>? handlerRegistry = null,
            string name = "ErrorHandling",
            string? description = null)
            : base(name, description ?? "通用错误处理 - 基于 IHandlerRegistry")
        {
            _handlerRegistry = handlerRegistry ?? CreateDefaultRegistry();
        }

        private static IHandlerRegistry<string, IErrorHandler> CreateDefaultRegistry()
        {
            var registry = new HandlerRegistry<string, IErrorHandler>();
            registry.Register("log", new LogErrorHandler());
            registry.Register("retry", new RetryErrorHandler(new RetryPolicy
            {
                MaxRetryCount = 3,
                RetryDelayMs = 1000,
                RetryDelayMultiplier = 2.0
            }));
            return registry;
        }

        protected virtual bool IsNodeFailed(IStageContext context, string nodeKey)
        {
            var records = GetData<List<NodeExecutionRecord>>(context, "NodeExecutionRecords");
            var nodeRecord = records?.FirstOrDefault(r => r.NodeKey == nodeKey);

            if (nodeRecord != null && nodeRecord.Success)
                return false;

            if (nodeRecord != null && !nodeRecord.Success)
                return true;

            var failedNodes = GetData<List<string>>(context, "FailedNodes");
            if (failedNodes != null && failedNodes.Contains(nodeKey))
                return true;

            return false;
        }

        protected virtual string GetNodeErrorMessage(IStageContext context, string nodeKey)
        {
            var records = GetData<List<NodeExecutionRecord>>(context, "NodeExecutionRecords");
            var nodeRecord = records?.FirstOrDefault(r => r.NodeKey == nodeKey);
            return nodeRecord?.ErrorMessage ?? "未知错误";
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var currentNodeKey = context.CurrentNodeKey;
            if (string.IsNullOrEmpty(currentNodeKey)) return;

            if (!IsNodeFailed(context, currentNodeKey))
                return;

            var errorMessage = GetNodeErrorMessage(context, currentNodeKey);

            var errorContext = new ErrorContext
            {
                ExecutionId = context.SessionId ?? Guid.NewGuid().ToString("N"),
                NodeKey = currentNodeKey,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.UtcNow,
                RetryCount = GetData<int>(context, $"RetryCount.{currentNodeKey}")
            };

            LogWarn(context, $"节点 {currentNodeKey} 执行失败: {errorContext.ErrorMessage}");

            foreach (var kvp in _handlerRegistry.GetAll())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var shouldContinue = await kvp.Value.HandleErrorAsync(
                        errorContext, context, cancellationToken);

                    if (!shouldContinue)
                    {
                        LogDebug(context, $"错误处理器 {kvp.Key} 要求停止处理");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    LogWarn(context, $"错误处理器 {kvp.Key} 执行失败: {ex.Message}");
                }
            }

            SetData(context, $"RetryCount.{currentNodeKey}", errorContext.RetryCount + 1);
        }

        public void RegisterHandler(string name, IErrorHandler handler)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名称不能为空", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _handlerRegistry.Register(name, handler);
        }

        public bool RemoveHandler(string name)
        {
            return _handlerRegistry.Remove(name);
        }


        private class LogErrorHandler : IErrorHandler
        {
            public Task<bool> HandleErrorAsync(
                ErrorContext errorContext,
                IStageContext context,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }
        }

        private class RetryErrorHandler : IErrorHandler
        {
            private readonly RetryPolicy _policy;

            public RetryErrorHandler(RetryPolicy policy)
            {
                _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            }

            public async Task<bool> HandleErrorAsync(
                ErrorContext errorContext,
                IStageContext context,
                CancellationToken cancellationToken)
            {
                if (errorContext.RetryCount >= _policy.MaxRetryCount)
                {
                    return false;
                }

                var delayMs = _policy.GetDelayMs(errorContext.RetryCount);
                await Task.Delay(delayMs, cancellationToken);
                return true;
            }
        }
    }
}
