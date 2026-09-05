namespace ExecutionFramework.Engine;

using Abstractions;
using TConfig;

public class ReloadResult
{
    public bool IsSuccess { get; set; }

    public string? Message { get; set; }

    public DateTime ReloadTime { get; set; } = DateTime.UtcNow;

    public string? ChangeSummary { get; set; }

    public static ReloadResult Success(string message, string? changeSummary = null)
        => new() { IsSuccess = true, Message = message, ChangeSummary = changeSummary };

    public static ReloadResult Failure(string message)
        => new() { IsSuccess = false, Message = message };
}

public interface IHotReloadService
{
    event EventHandler<ReloadResult>? OnReloaded;

    Task<ReloadResult> ReloadAsync();

    DateTime? LastReloadTime { get; }

    bool IsReloading { get; }
}

public class HotReloadService : IHotReloadService
{
    private readonly IExecutionLogger? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public DateTime? LastReloadTime { get; private set; }

    public bool IsReloading => _semaphore.CurrentCount == 0;

    public event EventHandler<ReloadResult>? OnReloaded;

    public HotReloadService(IExecutionLogger? logger = null)
    {
        _logger = logger;
    }

    public async Task<ReloadResult> ReloadAsync()
    {
        if (!await _semaphore.WaitAsync(0))
        {
            return ReloadResult.Failure("Reload is already in progress");
        }

        try
        {
            _logger?.LogInformation("Hot reload started");

            var result = await ReloadCoreAsync();

            if (result.IsSuccess)
            {
                LastReloadTime = DateTime.UtcNow;
                _logger?.LogInformation($"Hot reload completed: {result.Message}");
            }
            else
            {
                _logger?.LogError($"Hot reload failed: {result.Message}");
            }

            OnReloaded?.Invoke(this, result);
            return result;
        }
        catch (Exception ex)
        {
            var result = ReloadResult.Failure($"Hot reload failed: {ex.Message}");
            OnReloaded?.Invoke(this, result);
            return result;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected virtual Task<ReloadResult> ReloadCoreAsync()
    {
        return Task.FromResult(ReloadResult.Success("No reload logic implemented"));
    }
}
