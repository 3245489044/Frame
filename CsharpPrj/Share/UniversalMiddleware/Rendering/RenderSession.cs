
#nullable enable
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering;

public class RenderSession : IRenderSession
{
    private readonly Dictionary<string, object> _items = new();
    private readonly Dictionary<string, object> _diagnostics = new();

    public string SessionId { get; }

    public object? Target { get; }

    public object? Config { get; }

    public CancellationToken CancellationToken { get; }

    public bool IsCompleted { get; private set; }

    public Dictionary<string, object> Items => _items;

    public RenderSession(object? target, object? config, CancellationToken cancellationToken = default)
    {
        SessionId = Guid.NewGuid().ToString("N");
        Target = target;
        Config = config;
        CancellationToken = cancellationToken;
    }

    public async Task ExecuteStageAsync(string stageName, Func<Task> stageAction)
    {
        try
        {
            AddDiagnostic($"Stage_{stageName}_Start", DateTime.UtcNow);
            await stageAction();
            AddDiagnostic($"Stage_{stageName}_End", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Stage_{stageName}_Error", ex.Message);
            throw;
        }
    }

    public void AddDiagnostic(string key, object value)
    {
        _diagnostics[key] = value;
    }

    public void Complete()
    {
        IsCompleted = true;
    }

    public IReadOnlyDictionary<string, object> GetDiagnostics() => _diagnostics;
}
