using CSG.Unified;

namespace CSG.Unified;

public class TransformBatch : ITransformBatch

{
    private readonly ITransformEngine _engine;
    private readonly List<BatchStep> _steps = new();
    private readonly Dictionary<string, object?> _stepResults = new();
    private PlanOptimizer? _optimizer;

    public TransformBatch(ITransformEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public TransformBatch WithOptimizer(PlanOptimizer optimizer)
    {
        _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
        return this;
    }


    public TransformBatch AddStep(string stepName, string operationName, object input)
    {
        _steps.Add(new BatchStep(stepName, operationName, input));
        return this;
    }

    public TransformBatch AddStepFromPrevious(string stepName, string operationName, string referenceStepName)
    {
        _steps.Add(new BatchStep(stepName, operationName, referenceStepName, IsReference: true));
        return this;
    }

    public Dictionary<string, object?> Execute()
    {
        _stepResults.Clear();

        foreach (var step in _steps)
        {
            try
            {
                object input;

                if (step.IsReference)
                {
                    var referenceName = (string)step.Input;
                    if (!_stepResults.TryGetValue(referenceName, out var previousResult))
                    {
                        throw new InvalidOperationException(
                            $"步骤 '{step.StepName}' 引用的前一步 '{referenceName}' 不存在或尚未执行");
                    }
                    input = previousResult ?? throw new InvalidOperationException(
                        $"步骤 '{step.StepName}' 引用的前一步 '{referenceName}' 结果为 null");
                }
                else
                {
                    input = step.Input;
                }

                var result = _engine.CallOperation(step.OperationName, input);
                _stepResults[step.StepName] = result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"批处理步骤 '{step.StepName}' (操作: {step.OperationName}) 执行失败: {ex.Message}", ex);
            }
        }

        return new Dictionary<string, object?>(_stepResults);
    }

    public T? GetResult<T>(string stepName)
    {
        if (_stepResults.TryGetValue(stepName, out var result) && result is T typedResult)
            return typedResult;
        return default;
    }

    public void Clear()
    {
        _steps.Clear();
        _stepResults.Clear();
    }

    public int StepCount => _steps.Count;

    IReadOnlyList<StepData> ITransformBatch.GetStepData()
    {
        return _steps.Select(s => new StepData
        {
            StepName = s.StepName,
            OperationName = s.OperationName,
            Input = s.Input,
            IsReference = s.IsReference
        }).ToList().AsReadOnly();
    }

    private record BatchStep(
        string StepName,
        string OperationName,
        object Input,
        bool IsReference = false);
}



