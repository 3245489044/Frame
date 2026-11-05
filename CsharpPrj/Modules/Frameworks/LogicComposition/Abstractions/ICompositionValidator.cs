
using Share.LogicComposition.Models;

namespace Share.LogicComposition.Abstractions
{
    public interface ICompositionValidator
    {
        ValidationResult Validate(CompositionValidationContext context);
    }

    public class CompositionValidationContext
    {
        public string OperationType { get; set; } = string.Empty;

        public string SourceDescription { get; set; } = string.Empty;

        public string TargetDescription { get; set; } = string.Empty;

        public Dictionary<string, object> CustomRules { get; set; } = new();
    }
}
