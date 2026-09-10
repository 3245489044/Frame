
namespace Share.StateManagement.Stages
{
    public class FinalizeStage : StageBase
    {
        public FinalizeStage(string name = "Finalize", string? description = null)
            : base(name, description ?? "通用最终清理 - 设置完成状态和结束时间")
        {
        }

        protected override Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            SetData(context, "ExecutionStatus", "Completed");
            SetData(context, "ExecutionEndTime", DateTime.UtcNow);

            LogInfo(context, $"编排执行完成: Session={context.SessionId}");

            return Task.CompletedTask;
        }
    }
}
