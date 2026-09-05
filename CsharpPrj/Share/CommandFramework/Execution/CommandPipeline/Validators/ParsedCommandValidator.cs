
using CommandFramework.Parsing;
using FluentValidation;

namespace CommandFramework.Validators;

public class ParsedCommandValidator : AbstractValidator<ParsedCommand>
{
    public ParsedCommandValidator()
    {
        RuleFor(x => x.Verb)
            .NotEmpty().WithMessage("Command verb cannot be empty.")
            .MaximumLength(200).WithMessage("Command verb cannot exceed 200 characters.");

        RuleFor(x => x.NormalizedCommand)
            .NotEmpty().WithMessage("Normalized command cannot be empty.")
            .When(x => !x.IsNonEventCommand);


        RuleFor(x => x.Target)
            .MaximumLength(100).WithMessage("Target cannot exceed 100 characters.")
            .When(x => !string.IsNullOrEmpty(x.Target));

        RuleFor(x => x.Action)
            .MaximumLength(50).WithMessage("Action cannot exceed 50 characters.")
            .When(x => !string.IsNullOrEmpty(x.Action));
    }
}
