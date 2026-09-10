
using CommandFramework.Abstractions;
using CommandFramework.Models;
using BP = Modules.Pipeline;

namespace CommandFramework.Pipeline.Stages;

public class ScriptLoadStage : BP.PipelineStageBase<CommandContext>
{
    private readonly IScriptConverter? _scriptConverter;

    public ScriptLoadStage(IScriptConverter? scriptConverter = null)
        : base("ScriptLoad", "Load script and convert to command file")
    {
        _scriptConverter = scriptConverter;
    }

    public override bool CanExecute(CommandContext context)
    {
        return !string.IsNullOrWhiteSpace(context.ScriptPath);
    }

    protected override async Task ExecuteCoreAsync(CommandContext context, CancellationToken ct)
    {
        var scriptPath = context.ScriptPath!;

        if (!File.Exists(scriptPath))
        {
            context.ScriptLoadSuccess = false;
            context.ScriptLoadErrorMessage = $"Script file not found: {scriptPath}";
            return;
        }

        var extension = Path.GetExtension(scriptPath).ToLowerInvariant();
        string? commandFilePath = null;

        switch (extension)
        {
            case ".json":
                if (_scriptConverter == null)
                {
                    context.ScriptLoadSuccess = false;
                    context.ScriptLoadErrorMessage =
                        "JSON script requires an IScriptConverter. Register one via RegisterServices().";
                    return;
                }

                commandFilePath = await Task.Run(
                    () => _scriptConverter.ConvertJsonScriptToCommandFile(scriptPath), ct);

                if (commandFilePath == null || !File.Exists(commandFilePath))
                {
                    context.ScriptLoadSuccess = false;
                    context.ScriptLoadErrorMessage = $"Failed to convert JSON script: {scriptPath}";
                    return;
                }
                break;

            case ".txt":
                commandFilePath = scriptPath;
                break;

            default:
                context.ScriptLoadSuccess = false;
                context.ScriptLoadErrorMessage =
                    $"Unsupported script format: '{extension}'. Supported: .json, .txt";
                return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(commandFilePath, ct);
            context.CommandLines = lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("//"))
                .ToArray();

            context.CommandFilePath = commandFilePath;
            context.ScriptLoadSuccess = true;
        }
        catch (Exception ex)
        {
            context.ScriptLoadSuccess = false;
            context.ScriptLoadErrorMessage = $"Failed to read command file '{commandFilePath}': {ex.Message}";
        }
    }
}
