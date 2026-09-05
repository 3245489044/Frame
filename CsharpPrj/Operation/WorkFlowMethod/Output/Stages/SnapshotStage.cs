
using TConfig.ContextFrame.Abstractions;
using Modules.Pipeline;

namespace WorkFlowMethod.Output.Stages;

public class SnapshotStage : OutputStageBase
{
    private readonly IStorageEngine _storageEngine;

    public override string Name => "Snapshot";

    public override int Order => 5;

    public override bool IsFatal => false;

    public SnapshotStage(IStorageEngine storageEngine)
    {
        _storageEngine = storageEngine ?? throw new ArgumentNullException(nameof(storageEngine));
    }

    protected override object? ExecuteCore(OutputWriteContext ctx, ErrorCollector errors, PipelineContext context)
    {

        try
        {
            ctx.Snapshot = _storageEngine.CreateSnapshot(ctx.Storage.Name);
        }
        catch (Exception ex)
        {
            errors.Add("SNAPSHOT_FAILED",
                $"创建快照失败: {ex.Message}",
                ErrorLevel.Warning);
        }

        return ctx;
    }
}
