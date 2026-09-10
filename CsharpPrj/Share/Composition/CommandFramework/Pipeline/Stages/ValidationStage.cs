
using CommandFramework.Abstractions;
using FluentValidation;
using BP = Modules.Pipeline;

namespace CommandFramework.Pipeline.Stages;

public class ValidationStage<TContext> : PipelineStageBase<TContext>
    where TContext : BP.IPipelineContext
{
    private readonly IValidator<TContext> _validator;

    public ValidationStage(IValidator<TContext> validator, string name = "Validation")
        : base(name, "Validate context before execution")
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    protected override Task ExecuteCoreAsync(TContext context, CancellationToken cancellationToken)
    {
        var result = _validator.Validate(context);
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
            context.SetStageResult(Name, false, errors);
            return Task.CompletedTask;
        }

        context.SetStageResult(Name, true);
        return Task.CompletedTask;
    }
}
