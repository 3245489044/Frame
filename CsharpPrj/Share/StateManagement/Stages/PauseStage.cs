
using Share.StateManagement;
using Modules.StateMachine;

namespace Share.StateManagement.Stages
{
    public class PauseStage : StageBase
    {
        private readonly IPauseManager _pauseManager;

        public PauseStage(
            IPauseManager? pauseManager = null,
            string name = "Pause",
            string? description = null)
            : base(name, description ?? "通用暂停检查 - 支持全局/局部暂停")
        {
            _pauseManager = pauseManager ?? (IPauseManager)new PauseManager();
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            if (_pauseManager.IsGloballyPaused)
            {
                LogWarn(context, "检测到全局暂停，等待恢复...");
                await _pauseManager.WaitForResumeAsync(null, cancellationToken);
                LogInfo(context, "全局暂停已恢复");
            }

            var scopeId = context.SessionId;
            if (!string.IsNullOrEmpty(scopeId) && _pauseManager.IsLocallyPaused(scopeId))
            {
                LogWarn(context, $"检测到局部暂停 (Scope={scopeId})，等待恢复...");
                await _pauseManager.WaitForResumeAsync(scopeId, cancellationToken);
                LogInfo(context, $"局部暂停已恢复 (Scope={scopeId})");
            }
        }
    }
}
