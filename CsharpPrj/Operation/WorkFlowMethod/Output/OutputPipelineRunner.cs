
using TConfig.ContextFrame.Abstractions;
using Modules.Pipeline;
using Share.EventBindingOrchestrator.Abstractions;
using TConfig;

namespace WorkFlowMethod.Output;

public class OutputPipelineRunner
{
    private readonly Pipeline _pipeline;
    private readonly IStorageEngine _storageEngine;

    public OutputPipelineRunner(
        Pipeline pipeline,
        IStorageEngine storageEngine)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _storageEngine = storageEngine ?? throw new ArgumentNullException(nameof(storageEngine));
    }

    public OutputWriteResult Execute(object? value, EventContext? eventContext = null)
    {
        return Execute(eventContext?.Sender, eventContext?.OutputTarget, value);
    }

    public OutputWriteResult Execute(
        object sender,
        string? outputTarget,
        object? value)
    {
        var sessionId = $"output-{Guid.NewGuid():N}";
        var storageCell = _storageEngine.CreateSession(sessionId);

        try
        {
            var context = new OutputWriteContext
            {
                Sender = sender,
                OutputTarget = outputTarget,
                RawValue = value,
                Storage = storageCell
            };

            var result = _pipeline.Execute(context);

            return BuildResult(result, context);
        }
        finally
        {
            _storageEngine.DisposeSession(sessionId);
        }
    }

    public async Task<OutputWriteResult> ExecuteAsync(
        object sender,
        string? outputTarget,
        object? value,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"output-{Guid.NewGuid():N}";
        var storageCell = _storageEngine.CreateSession(sessionId);

        try
        {
            var context = new OutputWriteContext
            {
                Sender = sender,
                OutputTarget = outputTarget,
                RawValue = value,
                Storage = storageCell
            };

            var result = await _pipeline.ExecuteAsync(context, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return BuildResult(result, context);
        }
        finally
        {
            _storageEngine.DisposeSession(sessionId);
        }
    }

    public OutputWriteResult ExecuteWithStorage(
        object sender,
        string? outputTarget,
        object? value,
        IStorageCell storageCell)
    {
        var context = new OutputWriteContext
        {
            Sender = sender,
            OutputTarget = outputTarget,
            RawValue = value,
            Storage = storageCell
        };

        var result = _pipeline.Execute(context);
        return BuildResult(result, context);
    }

    private static OutputWriteResult BuildResult(object? pipelineResult, OutputWriteContext ctx)
    {
        return new OutputWriteResult
        {
            Success = ctx.WriteSuccess,
            ErrorMessage = ctx.ErrorMessage,
            TargetControl = ctx.TargetControl,
            FormattedValue = ctx.Storage?.Get<string>("FormattedValue", StorageLayer.Transient),
            HasExplicitConfig = ctx.Storage?.Get<bool>("HasExplicitConfig", StorageLayer.Transient) ?? false,
            StageSuccess = ctx.StageSuccess?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            StageErrors = ctx.StageErrors?
                .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(kvp =>
                        kvp.Value == null
                            ? Enumerable.Empty<string>()
                            : new[] { kvp.Value }).ToList(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }
}

public class OutputWriteResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public object? TargetControl { get; set; }

    public string? FormattedValue { get; set; }

    public bool HasExplicitConfig { get; set; }

    public Dictionary<string, bool>? StageSuccess { get; set; }

    public Dictionary<string, List<string>>? StageErrors { get; set; }
}
