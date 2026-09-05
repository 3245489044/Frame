
using BasicMethod.Declarative;
using BasicMethod.Declarative.Constraints;
using BasicMethod.Declarative.Resolvers;
using Modules.EventFramework.Models;
using TConfig;

namespace Modules.EventFramework;

public class EventBindingValidator<TParameter>
{
    private readonly DeclarationValidator<EventBindingDeclaration<TParameter>> _validator;

    public EventBindingValidator()
    {
        _validator = new DeclarationValidator<EventBindingDeclaration<TParameter>>("EventBindingValidator")
            .AddConstraint(new RequiredConstraint("TargetName", "目标对象名称不能为空"))
            .AddConstraint(new PredicateConstraint<EventBindingDeclaration<TParameter>>(
                "EventType", "事件类型不能为空",
                d => !string.IsNullOrWhiteSpace(d.EventType)))
            .AddConstraint(new PredicateConstraint<EventBindingDeclaration<TParameter>>(
                "FunctionName", "函数名称不能为空",
                d => !string.IsNullOrWhiteSpace(d.FunctionName)));
    }

    public EventBindingValidator(DeclarationValidator<EventBindingDeclaration<TParameter>> validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ValidationResult Validate(EventBindingDeclaration<TParameter> declaration)
        => _validator.Validate(declaration);

    public IReadOnlyList<ValidationResult> ValidateAll(IEnumerable<EventBindingDeclaration<TParameter>> declarations)
        => _validator.ValidateAll(declarations);

    public void ValidateOrThrow(EventBindingDeclaration<TParameter> declaration)
    {
        var result = Validate(declaration);
        if (!result.IsValid)
            throw new InvalidOperationException(
                $"事件绑定声明验证失败 [{declaration.Name}]: {string.Join("; ", result.FailureReasons)}");
    }
}
