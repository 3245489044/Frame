
using Modules.Transformers;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.Models;
using ValidationResult = Share.LogicComposition.Models.ValidationResult;

namespace Share.LogicComposition.DefaultImplementations
{
    public class DefaultCompositionValidator : ICompositionValidator
    {
        private readonly List<Validator<CompositionValidationContext>> _rules;

        public DefaultCompositionValidator()
        {
            _rules = BuildDefaultRules();
        }

        public DefaultCompositionValidator(List<Validator<CompositionValidationContext>> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        private static List<Validator<CompositionValidationContext>> BuildDefaultRules()
        {
            return new List<Validator<CompositionValidationContext>>
            {
                new(
                    "ContextNotNull",
                    ctx => ctx != null,
                    "验证上下文不能为 null"),

                new(
                    "OperationTypeSpecified",
                    ctx => ctx != null && !string.IsNullOrWhiteSpace(ctx.OperationType),
                    "操作类型未指定"),

                new(
                    "SourceDescriptionSpecified",
                    ctx => ctx != null && !string.IsNullOrWhiteSpace(ctx.SourceDescription),
                    "源对象描述为空"),

                new(
                    "TargetDescriptionSpecified",
                    ctx => ctx != null && !string.IsNullOrWhiteSpace(ctx.TargetDescription),
                    "目标对象描述为空"),
            };
        }

        public ValidationResult Validate(CompositionValidationContext context)
        {
            var result = new ValidationResult();

            foreach (var rule in _rules)
            {
                var r = rule.Transform(context);
                if (!r.IsValid)
                {
                    result.AddWarning(r.Error!);
                }
            }

            return result;
        }
    }
}
