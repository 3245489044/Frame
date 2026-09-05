
using System.Diagnostics;
using Container.Abstractions;
using Microsoft.Extensions.Logging;

namespace Container.Core;

public class ExecutionEngine : IExecutionEngine
{
    private readonly IExecutionCell _executionCell;
    private readonly ILogger<ExecutionEngine>? _logger;
    private readonly string _name;
    private ContainerStatus _status = ContainerStatus.Idle;
    private bool _disposed;

    public string Name => _name;

    public ContainerStatus Status
    {
        get => _status;
        private set
        {
            var oldStatus = _status;
            _status = value;
            OnStateChanged(new EngineStateChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = value,
                Message = $"状态变更: {oldStatus} → {value}"
            });
        }
    }

    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;

    public ExecutionEngine(
        IExecutionCell executionCell,
        ILogger<ExecutionEngine>? logger = null,
        string? name = null)
    {
        _executionCell = executionCell ?? throw new ArgumentNullException(nameof(executionCell));
        _logger = logger;
        _name = name ?? "ExecutionEngine";
    }

    public async Task<EngineExecutionResult> RunAsync(
        EngineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExecutionEngine));

        _logger?.LogInformation(
            "引擎 {Engine} 启动，模式: {Mode}",
            _name, request.Mode);

        var sw = Stopwatch.StartNew();
        Status = ContainerStatus.Running;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await InitializeAsync(request, cancellationToken);

            await ExecuteAsync(request, cancellationToken);

            sw.Stop();
            Status = ContainerStatus.Completed;

            _logger?.LogInformation(
                "引擎 {Engine} 执行完成，耗时 {Elapsed}ms",
                _name, sw.ElapsedMilliseconds);

            return new EngineExecutionResult(
                Success: true,
                TotalExecutionTime: sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            SetStatusDirect(ContainerStatus.Cancelled);

            _logger?.LogWarning(
                "引擎 {Engine} 被取消，耗时 {Elapsed}ms",
                _name, sw.ElapsedMilliseconds);

            return new EngineExecutionResult(
                Success: false,
                TotalExecutionTime: sw.Elapsed,
                ErrorMessage: "引擎执行被取消");
        }
        catch (Exception ex)
        {
            sw.Stop();
            SetStatusDirect(ContainerStatus.Failed);

            _logger?.LogError(ex,
                "引擎 {Engine} 执行失败，耗时 {Elapsed}ms",
                _name, sw.ElapsedMilliseconds);

            return new EngineExecutionResult(
                Success: false,
                TotalExecutionTime: sw.Elapsed,
                ErrorMessage: ex.Message);
        }
    }

    protected virtual Task InitializeAsync(
        EngineRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger?.LogDebug("引擎 {Engine} 初始化中...", _name);
        return Task.CompletedTask;
    }

    protected virtual async Task ExecuteAsync(
        EngineRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger?.LogDebug("引擎 {Engine} 执行中...", _name);

        var executionRequest = new ExecutionRequest(
            CommandText: $"Engine:{_name}",
            Parameters: request.GlobalParameters,
            Source: request.Mode,
            Mode: request.Mode switch
            {
                "GUI" => ExecutionMode.Gui,
                "CLI" => ExecutionMode.Cli,
                "Test" => ExecutionMode.Test,
                _ => ExecutionMode.Cli
            });

        await _executionCell.ExecuteAsync(executionRequest, cancellationToken);
    }

    protected virtual void OnStateChanged(EngineStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, args);
    }

    private void SetStatusDirect(ContainerStatus newStatus)
    {
        _status = newStatus;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_status == ContainerStatus.Running)
        {
            SetStatusDirect(ContainerStatus.Cancelled);
        }

        _disposed = true;
        _logger?.LogInformation("引擎 {Engine} 已释放", _name);
    }
}
