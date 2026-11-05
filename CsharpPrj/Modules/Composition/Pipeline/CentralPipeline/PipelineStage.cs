
using BP = Modules.Pipeline;

namespace Modules.CentralPipeline;

public enum PipelineStage
{
    Initialization = BP.PipelineStage.Initialize,

    PreProcessing = BP.PipelineStage.Load,

    Execution = BP.PipelineStage.Process,

    PostProcessing = BP.PipelineStage.Save,

    Finalization = BP.PipelineStage.Complete
}

public static class PipelineStageExtensions
{
    public static string GetDisplayName(this PipelineStage stage)
        => BP.PipelineStageExtensions.GetStageName((BP.PipelineStage)(int)stage);

    public static string GetStageKey(this PipelineStage stage)
        => ((BP.PipelineStage)(int)stage).ToString();

    public static IReadOnlyList<PipelineStage> GetAllStages()
        => BP.PipelineStageExtensions.GetAllStages()
            .Select(s => (PipelineStage)(int)s)
            .ToList().AsReadOnly();
}
