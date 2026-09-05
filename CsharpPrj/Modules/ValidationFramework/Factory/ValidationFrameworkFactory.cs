
using ValidationFramework.Abstractions;
using ValidationFramework.Contracts;
using ValidationFramework.Engine;
using ValidationFramework.Feedback;
using ValidationFramework.Pipeline;

namespace ValidationFramework.Factory;

public class ValidationFrameworkOptions : ValidationEngineOptions
{
    public ValidationMode Mode { get; set; } = ValidationMode.Loose;

    public bool EnableBuiltInContractRules { get; set; } = true;

    public new ValidationFrameworkOptions ShallowCopy()
    {
        var copy = (ValidationFrameworkOptions)MemberwiseClone();
        copy.AdditionalRules = AdditionalRules.ToList().AsReadOnly();
        return copy;
    }
}

public static class ValidationFrameworkFactory
{
    public static ValidationEngine CreateEngine(ValidationFrameworkOptions? options = null)
    {
        options ??= new ValidationFrameworkOptions();

        var engineOptions = options.ShallowCopy();
        engineOptions.Name = $"{engineOptions.Name}Engine";

        if (engineOptions.EnableBuiltInContractRules)
        {
            var additional = engineOptions.AdditionalRules.ToList();
            additional.AddRange(CreateBuiltInContractRules());
            engineOptions.AdditionalRules = additional.AsReadOnly();
        }

        return engineOptions.Build();
    }

    public static ValidationPipeline CreatePipeline(ValidationFrameworkOptions? options = null)
    {
        options ??= new ValidationFrameworkOptions();

        var pipelineOptions = options.ShallowCopy();
        var engine = CreateEngine(pipelineOptions);

        var builder = new ValidationPipelineBuilder($"{pipelineOptions.Name}Pipeline");
        builder.AddStage("Validation", engine);

        return builder.Build(pipelineOptions.Mode);
    }

    public static ValidationFeedbackFormatter CreateFeedbackFormatter(bool includeInfo = false)
        => new(includeInfo);

    public static IValidationFeedbackSink CreateFeedbackSink(Action<string>? logger = null)
        => new DefaultFeedbackSink(logger);

    private static IReadOnlyList<IValidationRule> CreateBuiltInContractRules()
        => new IValidationRule[]
        {
            new FunctionContractRule(),
            new WorkflowLegalityRule(),
            new ScopeGuardRule()
        };
}