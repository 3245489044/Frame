
using FluentValidation;
using TConfig;

namespace Share.EventBindingOrchestrator.Validation;

public class EventBindingContextValidator : AbstractValidator<EventBindingContext>
{
    public EventBindingContextValidator()
    {
        RuleFor(x => x.TargetObject)
            .NotNull()
            .WithMessage("ValidateInput 失败: TargetObject 为 null");

        When(x => x.CurrentDeclaration == null, () =>
        {
            RuleFor(x => x.Config)
                .NotNull()
                .WithMessage("ValidateInput 失败: Config 为 null（非 Declaration 路径需要 Config）");
        });
    }
}
