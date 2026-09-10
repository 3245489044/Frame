
namespace Share.StateManagement.Stages
{
    public class InitStage : StageBase
    {
        public InitStage(string name = "Init", string? description = null)
            : base(name, description ?? "通用初始化 - 设置执行开始时间、状态、入口节点")
        {
        }

        protected override Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            SetData(context, "ExecutionStartTime", DateTime.UtcNow);
            SetData(context, "ExecutionStatus", "Running");

            if (string.IsNullOrEmpty(context.CurrentNodeKey) && context.NodeKeys?.Count > 0)
            {
                context.CurrentNodeKey = context.NodeKeys[0];
            }

            LogInfo(context, $"初始化完成: Session={context.SessionId}, EntryNode={context.CurrentNodeKey}");
            return Task.CompletedTask;
        }
    }
}
