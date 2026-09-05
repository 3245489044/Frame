using System.Collections.Concurrent;
using System.Threading;

namespace ExecutionFramework.Engine;

using TConfig;
using Abstractions;

public class ExecutionEngine : TConfig.IExecutionEngine
{
    private readonly IExecutionOrchestrator _orchestrator;
    private readonly ISessionManager _sessionManager;
    private readonly IExecutionLogger? _logger;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeExecutions = new();

    private readonly ConcurrentDictionary<string, ManualResetEventSlim> _pauseEvents = new();

    public ExecutionEngine(
        IExecutionOrchestrator orchestrator,
        ISessionManager sessionManager,
        IExecutionLogger? logger = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var session = _sessionManager.CreateSession(plan);

        _logger?.LogExecutionStart(plan.Id, plan.Name);

        var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_activeExecutions.TryAdd(plan.Id, executionCts))
        {
            executionCts.Dispose();
            _logger?.LogWarning($"Execution '{plan.Id}' is already running");
            return TConfig.ExecutionResult.Failure($"Execution '{plan.Id}' is already running");
        }

        var pauseEvent = new ManualResetEventSlim(true);
        _pauseEvents[plan.Id] = pauseEvent;

        context.Items[ExecutionPauseCoordinator.PauseEventKey] = pauseEvent;

        try
        {
            session.MarkRunning();

            var result = await _orchestrator.OrchestrateAsync(plan, context, executionCts.Token);

            UpdateSessionFromResult(session, result);

            return result;
        }
        catch (OperationCanceledException)
        {
            session.MarkCancelled();
            _logger?.LogExecutionError(plan.Id, "Execution was cancelled");
            return TConfig.ExecutionResult.Cancelled();
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex.Message, ex);
            _logger?.LogExecutionError(plan.Id, ex.Message, ex);
            return TConfig.ExecutionResult.Failure($"Execution failed: {ex.Message}", ex);
        }
        finally
        {
            if (_activeExecutions.TryRemove(plan.Id, out var cts))
            {
                cts.Dispose();
            }
            if (_pauseEvents.TryRemove(plan.Id, out var pe))
            {
                pe.Set();
                pe.Dispose();
            }
        }
    }

    public Task CancelAsync(string planId)
    {
        var session = FindSession(planId);
        if (session != null)
        {
            session.MarkCancelled();
            _logger?.LogInformation($"Execution '{planId}' cancelled");
        }

        if (_activeExecutions.TryGetValue(planId, out var cts))
        {
            cts.Cancel();
            _logger?.LogInformation($"Execution '{planId}' cancellation signal sent");
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(string planId)
    {
        var session = FindSession(planId);
        if (session == null)
        {
            _logger?.LogWarning($"Execution '{planId}' not found for pause");
            return Task.CompletedTask;
        }

        session.MarkPaused();
        _logger?.LogInformation($"Execution '{planId}' paused");

        if (_pauseEvents.TryGetValue(planId, out var pauseEvent))
        {
            pauseEvent.Reset();
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync(string planId)
    {
        var session = FindSession(planId);
        if (session == null)
        {
            _logger?.LogWarning($"Execution '{planId}' not found for resume");
            return Task.CompletedTask;
        }

        session.MarkRunning();
        _logger?.LogInformation($"Execution '{planId}' resumed");

        if (_pauseEvents.TryGetValue(planId, out var pauseEvent))
        {
            pauseEvent.Set();
        }

        return Task.CompletedTask;
    }

    public ExecutionStatus GetStatus(string planId)
    {
        var session = FindSession(planId);
        return session?.Status ?? ExecutionStatus.Pending;
    }

    private IExecutionSession? FindSession(string planId)
    {
        return _sessionManager.GetSession(planId);
    }

    private void UpdateSessionFromResult(IExecutionSession session, TConfig.ExecutionResult result)
    {
        if (result.IsSuccess)
        {
            session.MarkCompleted();
            _logger?.LogExecutionComplete(session.WorkflowId, session.GetExecutionTime());
        }
        else if (result.IsCancelled)
        {
            session.MarkCancelled();
            _logger?.LogExecutionError(session.WorkflowId, result.ErrorMessage ?? "Execution was cancelled");
        }
        else
        {
            session.MarkFailed(result.ErrorMessage, result.Exception);
            _logger?.LogExecutionError(session.WorkflowId, result.ErrorMessage ?? "Unknown error", result.Exception);
        }
    }
}
