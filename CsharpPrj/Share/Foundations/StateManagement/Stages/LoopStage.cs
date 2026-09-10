
namespace Share.StateManagement.Stages
{
    public delegate Task<NodeExecutionResult> NodeProcessor(
        string nodeKey,
        IStageContext context,
        CancellationToken cancellationToken);

    public class LoopStage : StageBase
    {
        private readonly NodeProcessor _nodeProcessor;
        private readonly Func<IStageContext, bool>? _condition;

        public LoopStage(
            NodeProcessor? nodeProcessor = null,
            Func<IStageContext, bool>? condition = null,
            string name = "Loop",
            string? description = null)
            : base(name, description ?? "通用循环执行 - 支持条件循环和节点迭代")
        {
            _nodeProcessor = nodeProcessor ?? DefaultNodeProcessor;
            _condition = condition;
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var nodeKeys = context.NodeKeys;
            if (nodeKeys == null || nodeKeys.Count == 0)
            {
                LogWarn(context, "没有节点需要循环执行");
                return;
            }

            var maxIterations = GetData<int>(context, "MaxIterations");
            if (maxIterations <= 0) maxIterations = nodeKeys.Count;

            var records = new List<NodeExecutionRecord>();
            var iterationCount = 0;

            LogInfo(context, $"开始循环执行: {maxIterations} 次迭代, {nodeKeys.Count} 个节点");

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var nodeKey in nodeKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    context.CurrentNodeKey = nodeKey;

                    var record = new NodeExecutionRecord
                    {
                        NodeKey = nodeKey,
                        StartTime = DateTime.UtcNow
                    };

                    try
                    {
                        var result = await _nodeProcessor(nodeKey, context, cancellationToken);
                        record.Success = result.Success;
                        record.ErrorMessage = result.Error?.Message;

                        if (!result.Success)
                        {
                            LogWarn(context, $"节点执行失败: {nodeKey}, Error={record.ErrorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        record.Success = false;
                        record.ErrorMessage = ex.Message;
                        LogError(context, $"节点执行异常: {nodeKey}", ex);
                    }
                    finally
                    {
                        record.EndTime = DateTime.UtcNow;
                    }

                    records.Add(record);
                    iterationCount++;
                }

                if (!ShouldContinue(context, iteration, maxIterations))
                {
                    break;
                }
            }

            SetData(context, "NodeExecutionRecords", records);
            SetData(context, "TotalIterations", iterationCount);

            LogInfo(context, $"循环执行完成: {iterationCount} 次执行, {records.Count(r => !r.Success)} 次失败");
        }

        protected virtual bool ShouldContinue(IStageContext context, int currentIteration, int maxIterations)
        {
            if (_condition != null)
            {
                return _condition(context);
            }

            return currentIteration + 1 < maxIterations;
        }

        private static Task<NodeExecutionResult> DefaultNodeProcessor(
            string nodeKey,
            IStageContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Result = null
            });
        }
    }
}
