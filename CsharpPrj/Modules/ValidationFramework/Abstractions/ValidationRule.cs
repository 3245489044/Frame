
using System.Text.Json;

namespace ValidationFramework.Abstractions;

public enum ValidationMode
{
    Loose = 0,

    Strict = 1,

    Silent = 2
}

public interface IValidationContext
{
    ValidationMode Mode { get; }

    string? FilePath { get; }

    string? RawJson { get; }

    bool IsSilent => Mode == ValidationMode.Silent;

    bool IsStrict => Mode == ValidationMode.Strict;

    TProvider? GetProvider<TProvider>() where TProvider : class;
}

public class ValidationContext : IValidationContext
{
    private readonly Dictionary<Type, object> _providers = new();

    public ValidationMode Mode { get; }

    public string? FilePath { get; set; }

    public string? RawJson { get; set; }

    public ValidationContext(
        ValidationMode mode = ValidationMode.Loose,
        string? filePath = null,
        string? rawJson = null)
    {
        Mode = mode;
        FilePath = filePath;
        RawJson = rawJson;
    }

    public ValidationContext WithProvider<TProvider>(TProvider provider)
        where TProvider : class
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        _providers[typeof(TProvider)] = provider;
        return this;
    }

    public ValidationContext WithProviders(IEnumerable<object> providers)
    {
        foreach (var provider in providers)
        {
            if (provider != null)
            {
                _providers[provider.GetType()] = provider;
            }
        }
        return this;
    }

    public TProvider? GetProvider<TProvider>() where TProvider : class
        => _providers.TryGetValue(typeof(TProvider), out var value)
            ? value as TProvider
            : null;

    public object? GetProvider(Type providerType)
        => _providers.TryGetValue(providerType, out var value) ? value : null;
}

public enum ValidationRuleCategory
{
    Structure = 0,

    Reference = 1,

    Semantic = 2,

    Runtime = 3,

    Contract = 4
}

public interface IValidationRule
{
    string RuleName { get; }

    string Description { get; }

    ValidationRuleCategory Category { get; }

    Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
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
        IValidationContext context,
        CancellationToken cancellationToken = default);

    protected ValidationDiagnostic Error(
        string message,
        SourceLocation? location = null,
        string? suggestion = null)
        => ValidationDiagnostic.Error(RuleName, message, location, suggestion);

    protected ValidationDiagnostic Warning(
        string message,
        SourceLocation? location = null,
        string? suggestion = null)
        => ValidationDiagnostic.Warning(RuleName, message, location, suggestion);

    protected ValidationDiagnostic Info(
        string message,
        SourceLocation? location = null,
        string? suggestion = null)
        => ValidationDiagnostic.Info(RuleName, message, location, suggestion);
}