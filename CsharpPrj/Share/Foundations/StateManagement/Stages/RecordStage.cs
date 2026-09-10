
namespace Share.StateManagement.Stages
{
    public class RecordStage : StageBase
    {
        public RecordStage(string name = "Record", string? description = null)
            : base(name, description ?? "通用执行记录汇总 - 统计节点执行结果")
        {
        }

        protected override Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var records = GetData<List<NodeExecutionRecord>>(context, "NodeExecutionRecords");
            var startTime = GetData<DateTime?>(context, "ExecutionStartTime");

            var totalNodes = records?.Count ?? 0;
            var failedNodes = records?.Count(r => !r.Success) ?? 0;
            var duration = startTime.HasValue
                ? DateTime.UtcNow - startTime.Value
                : TimeSpan.Zero;

            SetData(context, "TotalNodes", totalNodes);
            SetData(context, "FailedNodes", failedNodes);
            SetData(context, "TotalDuration", duration);

            LogInfo(context, $"执行汇总: 共 {totalNodes} 节点, {failedNodes} 失败, 耗时 {duration.TotalSeconds:F2}s");

            return Task.CompletedTask;
        }
    }
}
