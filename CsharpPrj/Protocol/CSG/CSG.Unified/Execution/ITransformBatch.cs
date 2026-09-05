namespace CSG.Unified;

public interface ITransformBatch
{
    int StepCount { get; }

    IReadOnlyList<StepData> GetStepData();
}

