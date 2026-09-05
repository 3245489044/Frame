
using System.Text.Json;
using BasicMethod.Declarative;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Declarative;

public class ConstraintValidationRule<T> : ValidationRuleBase
{
    private readonly IConstraint<T> _constraint;
    private readonly Func<JsonElement, IValidationContext, T?> _extractor;
    private readonly Func<JsonElement, IValidationContext, string?>? _pathSelector;

    public ConstraintValidationRule(
        IConstraint<T> constraint,
        Func<JsonElement, IValidationContext, T?> extractor,
        ValidationRuleCategory category = ValidationRuleCategory.Contract,
        Func<JsonElement, IValidationContext, string?>? pathSelector = null)
        : base(constraint.Name, constraint.Description, category)
    {
        _constraint = constraint ?? throw new ArgumentNullException(nameof(constraint));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _pathSelector = pathSelector;
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        var target = _extractor(config, context);
        if (target == null)
        {
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = _constraint.Check(target);
        if (!result.IsSatisfied)
        {
            var jsonPath = _pathSelector?.Invoke(config, context);
            diagnostics.Add(Error(
                result.FailureReason ?? $"不满足约束 '{RuleName}'",
                new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath),
                $"约束 '{Description}' 失败"));
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}

public static class ConstraintRuleFactory
{
    public static ConstraintValidationRule<T> Predicate<T>(
        string ruleName,
        string description,
        Func<T, bool> predicate,
        Func<JsonElement, IValidationContext, T?> extractor,
        Func<T, string>? failureReasonFactory = null,
        ValidationRuleCategory category = ValidationRuleCategory.Contract)
    {
        var constraint = new BasicMethod.Declarative.Constraints.PredicateConstraint<T>(
            ruleName, description, predicate, failureReasonFactory);
        return new ConstraintValidationRule<T>(constraint, extractor, category);
    }
}