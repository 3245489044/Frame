
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation;

public interface IConfigValidator<TConfig>
{
    string Name { get; }

    Task<ValidationReport> ValidateAsync(
        TConfig config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default);
}

public class CompositeConfigValidator<TConfig> : IConfigValidator<TConfig>
{
    private readonly List<IConfigValidator<TConfig>> _validators = new();

    public string Name { get; }

    public CompositeConfigValidator(string name, IEnumerable<IConfigValidator<TConfig>>? validators = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (validators != null)
        {
            _validators.AddRange(validators);
        }
    }

    public CompositeConfigValidator<TConfig> AddValidator(IConfigValidator<TConfig> validator)
    {
        if (validator == null) throw new ArgumentNullException(nameof(validator));
        _validators.Add(validator);
        return this;
    }

    public async Task<ValidationReport> ValidateAsync(
        TConfig config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var report = new ValidationReport(Name);

        foreach (var validator in _validators)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subReport = await validator.ValidateAsync(config, context, cancellationToken);
            report.MergeFrom(subReport);
        }

        return report;
    }
}
