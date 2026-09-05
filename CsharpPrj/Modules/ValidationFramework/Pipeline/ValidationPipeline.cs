
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Engine;

namespace ValidationFramework.Pipeline;

public enum PipelineStageState
{
    Pending = 0,

    Running = 1,

    Completed = 2,

    Failed = 3
}

public class ValidationPipelineContext
{
    private readonly List<ValidationDiagnostic> _allDiagnostics = new();
    private readonly object _lock = new();
    private int _errorCount;
    private int _warningCount;
    private int _infoCount;

    public string PipelineName { get; }

    public JsonElement Config { get; }

    public IValidationContext Context { get; }

    public IReadOnlyDictionary<string, PipelineStageState> StageStates { get; }

    private readonly Dictionary<string, PipelineStageState> _stageStates = new(StringComparer.Ordinal);

    public IReadOnlyList<ValidationDiagnostic> Diagnostics
    {
        get
        {
            lock (_lock) return _allDiagnostics.ToList().AsReadOnly();
        }
    }

    public int ErrorCount
    {
        get { lock (_lock) return _errorCount; }
    }

    public int WarningCount
    {
        get { lock (_lock) return _warningCount; }
    }

    public int InfoCount
    {
        get { lock (_lock) return _infoCount; }
    }

    public bool IsValid => ErrorCount == 0;

    public ValidationPipelineContext(
        string pipelineName,
        JsonElement config,
        IValidationContext context)
    {
        PipelineName = pipelineName;
        Config = config;
        Context = context;
        StageStates = new ReadOnlyDictionary<string, PipelineStageState>(_stageStates);
    }

    public void BeginStage(string stageName)
    {
        lock (_lock)
        {
            _stageStates[stageName] = PipelineStageState.Running;
        }
    }

    public void CompleteStage(string stageName, IEnumerable<ValidationDiagnostic> diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

        lock (_lock)
        {
            var hasError = false;
            foreach (var d in diagnostics)
            {
                _allDiagnostics.Add(d);
                switch (d.Severity)
                {
                    case Severity.Error: _errorCount++; hasError = true; break;
                    case Severity.Warning: _warningCount++; break;
                    case Severity.Info: _infoCount++; break;
                }
            }

            _stageStates[stageName] = hasError
                ? PipelineStageState.Failed
                : PipelineStageState.Completed;
        }
    }

    public ValidationReport BuildReport(TimeSpan duration)
    {
        lock (_lock)
        {
            return ValidationReport.Create(PipelineName, _allDiagnostics, duration, _errorCount, _warningCount, _infoCount);
        }
    }
}

public class ValidationPipeline
{
    private readonly IReadOnlyList<KeyValuePair<string, ValidationEngine>> _stageEngines;
    private readonly ValidationMode _mode;

    public string Name { get; }

    public ValidationPipeline(
        string name,
        IReadOnlyList<KeyValuePair<string, ValidationEngine>> stageEngines,
        ValidationMode mode = ValidationMode.Loose)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _stageEngines = stageEngines ?? throw new ArgumentNullException(nameof(stageEngines));
        _mode = mode;
    }

    public async Task<ValidationReport> ExecuteAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var pipelineContext = new ValidationPipelineContext(Name, config, context);

        var effectiveContext = MergeStrictMode(context);

        foreach (var (stageName, engine) in _stageEngines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            pipelineContext.BeginStage(stageName);
            var report = await engine.ValidateAsync(config, effectiveContext, cancellationToken);

            pipelineContext.CompleteStage(stageName, report.Diagnostics);

            if (effectiveContext.IsStrict && report.ErrorCount > 0)
            {
                break;
            }
        }

        stopwatch.Stop();
        return pipelineContext.BuildReport(stopwatch.Elapsed);
    }

    private IValidationContext MergeStrictMode(IValidationContext context)
    {
        if (_mode != ValidationMode.Strict || context.IsStrict || context.IsSilent)
        {
            return context;
        }
        return new StrictOverrideContext(context);
    }

    private sealed class StrictOverrideContext : IValidationContext
    {
        private readonly IValidationContext _inner;

        public StrictOverrideContext(IValidationContext inner)
        {
            _inner = inner;
        }

        public ValidationMode Mode => ValidationMode.Strict;

        public string? FilePath => _inner.FilePath;

        public string? RawJson => _inner.RawJson;

        public bool IsSilent => false;

        public bool IsStrict => true;

        public TProvider? GetProvider<TProvider>() where TProvider : class
            => _inner.GetProvider<TProvider>();
    }

    public async Task<ValidationReport> ExecuteJsonAsync(
        string json,
        ValidationMode mode = ValidationMode.Loose,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(json);
        var context = new ValidationContext(mode, filePath, json);
        return await ExecuteAsync(doc.RootElement, context, cancellationToken);
    }
}

public class ValidationPipelineBuilder
{
    private readonly List<KeyValuePair<string, ValidationEngine>> _stages = new();
    private readonly string _name;

    public ValidationPipelineBuilder(string name = "ValidationPipeline")
    {
        _name = name;
    }

    public ValidationPipelineBuilder AddStage(string stageName, ValidationEngine engine)
    {
        if (stageName == null) throw new ArgumentNullException(nameof(stageName));
        if (engine == null) throw new ArgumentNullException(nameof(engine));

        if (_stages.Any(kv => string.Equals(kv.Key, stageName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"阶段 '{stageName}' 已注册");
        }

        _stages.Add(new KeyValuePair<string, ValidationEngine>(stageName, engine));
        return this;
    }

    public ValidationPipeline Build(ValidationMode mode = ValidationMode.Loose)
        => new(_name, _stages.AsReadOnly(), mode);
}