
using CommandFramework.Models;
using FluentValidation;

namespace CommandFramework.Validators;

public class CommandContextValidator : AbstractValidator<CommandContext>
{
    public CommandContextValidator()
    {
        RuleFor(x => x.CurrentCommand)
            .NotEmpty().WithMessage("Current command cannot be empty.")
            .MaximumLength(1000).WithMessage("Command length cannot exceed 1000 characters.");

        RuleFor(x => x.CurrentCommandLineNumber)
            .GreaterThanOrEqualTo(0).WithMessage("Line number must be non-negative.");

        RuleFor(x => x.ParsedCommand)
            .SetValidator(new ParsedCommandValidator()!)
            .When(x => x.ParsedCommand != null);

        RuleFor(x => x.VirtualStore)
            .NotNull().WithMessage("VirtualStore cannot be null in CLI mode.")
            .When(x => x.IsCliMode);

        RuleFor(x => x.SharedData)
            .NotNull().WithMessage("SharedData cannot be null.");
    }
}
