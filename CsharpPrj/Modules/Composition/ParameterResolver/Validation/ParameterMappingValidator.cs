
using BasicMethod.Declarative;
using BasicMethod.Declarative.Constraints;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Validation;

public class ParameterMappingValidator
{
    public static readonly IReadOnlySet<string> KnownSourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "control",
        "literal",
        "expression"
    };

    private readonly string _name;
    private readonly List<IConstraint<IReadOnlyList<ParameterMapping>>> _constraints = new();

    public string Name => _name;

    public int ConstraintCount => _constraints.Count;

    public ParameterMappingValidator()
        : this("ParameterMappingValidator")
    {
        AddDefaultConstraints();
    }

    public ParameterMappingValidator(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public ParameterMappingValidator AddConstraint(IConstraint<IReadOnlyList<ParameterMapping>> constraint)
    {
        if (constraint == null) throw new ArgumentNullException(nameof(constraint));
        _constraints.Add(constraint);
        return this;
    }

    public ParameterMappingValidator AddConstraints(IEnumerable<IConstraint<IReadOnlyList<ParameterMapping>>> constraints)
    {
        if (constraints == null) throw new ArgumentNullException(nameof(constraints));
        _constraints.AddRange(constraints);
        return this;
    }

    public ValidationResult Validate(IReadOnlyList<ParameterMapping> mappings)
    {
        var results = _constraints.Select(c => c.Check(mappings));
        return new ValidationResult(_name, results);
    }

    public void ValidateOrThrow(IReadOnlyList<ParameterMapping> mappings)
    {
        var result = Validate(mappings);
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.FailureReasons);
            throw new InvalidOperationException($"参数映射验证失败: {errors}");
        }
    }

    private void AddDefaultConstraints()
    {
        _constraints.Add(new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
            "Target", "参数映射中的 Target 不能为空",
            mappings => mappings.All(m => !string.IsNullOrWhiteSpace(m.Target))));

        _constraints.Add(new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
            "Target", "参数映射中的 Target 不能重复",
            mappings =>
            {
                if (mappings.Count == 0)
                    return true;
                return mappings.Select(p => p.Target)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == mappings.Count;
            }));

        _constraints.Add(new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
            "SourceType", "SourceType 必须是已知类型（control/literal/expression）",
            mappings => mappings.All(m => string.IsNullOrEmpty(m.SourceType) || KnownSourceTypes.Contains(m.SourceType))));

        _constraints.Add(new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
            "Source", "当 SourceType 为 control 时，Source 不能为空",
            mappings => mappings.All(m =>
                m.SourceType != "control" || !string.IsNullOrWhiteSpace(m.Source))));
    }
}
