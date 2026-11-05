
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules;

public enum ValidationRuleCategory
{
    Structure,

    Reference,

    Semantic,

    Runtime
}

public interface IValidationRule
{
    string RuleName { get; }

    string Description { get; }

    ValidationRuleCategory Category { get; }

    Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default);
}

public abstract class ValidationRuleBase : IValidationRule
{
    public string RuleName { get; }

    public string Description { get; }

    public ValidationRuleCategory Category { get; }

    protected ValidationRuleBase(
        string ruleName,
        string description,
        ValidationRuleCategory category)
    {
        RuleName = ruleName ?? throw new ArgumentNullException(nameof(ruleName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category;
    }

    public abstract Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default);

    protected ValidationDiagnostic Error(string message)
        => ValidationDiagnostic.CreateError(RuleName, message);

    protected ValidationDiagnostic Warning(string message)
        => ValidationDiagnostic.CreateWarning(RuleName, message);

    protected ValidationDiagnostic Info(string message)
        => ValidationDiagnostic.CreateInfo(RuleName, message);
}
