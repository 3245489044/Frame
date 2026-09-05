
using TConfig.ContextFrame.Abstractions;
using Modules.Pipeline;

namespace WorkFlowMethod.Output.Stages;

public class RollbackStage : OutputStageBase
{
    private readonly IStorageEngine _storageEngine;

    public override string Name => "Rollback";

    public override int Order => 60;

    public override bool IsFatal => false;

    public RollbackStage(IStorageEngine storageEngine)
    {
        _storageEngine = storageEngine ?? throw new ArgumentNullException(nameof(storageEngine));
    }

    protected override object? ExecuteCore(OutputWriteContext ctx, ErrorCollector errors, PipelineContext context)
    {

        if (ctx.WriteSuccess)
        {
            return ctx;
        }

        var snapshot = ctx.Snapshot;
        if (snapshot == null)
        {
            errors.Add("ROLLBACK_NO_SNAPSHOT",
                "写入失败但未找到快照，无法回滚",
                ErrorLevel.Warning);
            return ctx;
        }

        try
        {
            _storageEngine.RestoreSnapshot(ctx.Storage.Name, snapshot);

            errors.Add("ROLLBACK_SUCCESS",
                "写入失败，已从快照恢复",
                ErrorLevel.Warning);
        }
        catch (Exception ex)
        {
            errors.Add("ROLLBACK_FAILED",
                $"回滚失败: {ex.Message}",
                ErrorLevel.Error,
                ex);
        }

        return ctx;
    }
}
