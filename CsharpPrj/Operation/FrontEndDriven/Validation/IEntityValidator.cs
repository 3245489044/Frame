
#nullable enable
using ECS.Core;
using ECS.Bridge;

namespace FrontEndDriven.Validation;

public enum ValidationCategory
{
    Completeness,

    Layout,

    Style,

    EventBinding,

    Custom
}

public enum ValidationSeverity
{
    Error,

    Warning
}

public class ValidationContext
{
    public ControlPool? ControlPool { get; set; }

    public Dictionary<string, object?> CustomData { get; } = new();
}

public interface IEntityValidator
{
    string ValidatorName { get; }

    ValidationCategory Category { get; }

    void Validate(Entity entity, EntityManager entityManager,
                  ValidationContext context, ValidationResult result);
}
