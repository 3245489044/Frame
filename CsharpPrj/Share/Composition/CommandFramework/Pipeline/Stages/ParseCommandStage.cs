
using CommandFramework.Models;
using CommandFramework.Parsing;

namespace CommandFramework.Pipeline.Stages;

public class ParseCommandStage : CommandStageBase
{
    private readonly CommandParser _parser;

    public ParseCommandStage(CommandParser parser)
        : base("ParseCommand", "Parse command string")
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    protected override Task ExecuteCoreAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var command = context.CurrentCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            context.SetStageResult(Name, false, "CurrentCommand is empty");
            return Task.CompletedTask;
        }

        var parsed = _parser.Parse(command);
        context.ParsedCommand = parsed;

        if (!parsed.IsSuccess)
        {
            context.SetStageResult(Name, false, parsed.ErrorMessage ?? "Command parsing failed");
            return Task.CompletedTask;
        }

        context.SetStageResult(Name, true);
        return Task.CompletedTask;
    }
}
