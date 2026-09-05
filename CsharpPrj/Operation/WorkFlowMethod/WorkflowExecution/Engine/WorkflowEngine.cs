using System.Collections.Concurrent;
using System.Diagnostics;
using CommandFramework.Execution;

namespace WorkflowExecution.Engine;

using Abstractions;
using ExecutionFramework.Engine;
using ExecutionFramework.Session;
using ExecutionFramework.Abstractions;
using ExecutionFrameworkStatus = TConfig.ExecutionStatus;
using Pipeline;
using WorkFlowMethod.Diagnostics;


public class WorkflowEngine : TConfig.IExecutionEngine
{
    private readonly CentralPipeline _pipeline;
    private readonly ISessionManager _sessionManager;

    private readonly CancellationTokenSourceManager _ctsManager = new();

    public WorkflowEngine(
        CentralPipeline pipeline,
        ISessionManager? sessionManager = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _sessionManager = sessionManager ?? new DefaultSessionManager(new DefaultSessionFactory());
    }

    public async Task<TConfig.ExecutionResult> ExecuteAsync(
        TConfig.ExecutionPlan workflow,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCoreAsync(workflow, context, cancellationToken);
    }

    async Task<TConfig.ExecutionResult> TConfig.IExecutionEngine.ExecuteAsync(
        TConfig.ExecutionPlan plan,
        TConfig.ExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await ExecuteCoreAsync(plan, context, cancellationToken);
    }

    private async Task<TConfig.ExecutionResult> ExecuteCoreAsync(
        TConfig.ExecutionPlan workflow,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        var session = _sessionManager.CreateSession(workflow, context.ExecutionId);
        session.MarkRunning();

        var cts = _ctsManager.Register(session.SessionId, cancellationToken);

        var sw = Stopwatch.StartNew();
        WorkFlowMethodLog.Log("WorkflowEngine_ExecutionStarted",
            new { workflowId = workflow.Id, workflowName = workflow.Name, sessionId = session.SessionId });

        try
        {
            context.SetData("ExecutionPlan", workflow);
            using var pipelineSession = _pipeline.CreateSession();
            var result = await pipelineSession.ExecuteAsync(workflow, context, cts.Token);

            sw.Stop();
            if (result.IsSuccess)
            {
                session.MarkCompleted();
                WorkFlowMethodLog.Log("WorkflowEngine_ExecutionCompleted",
                    new { workflowId = workflow.Id, elapsedMs = sw.ElapsedMilliseconds });
            }
            else
            {
                session.MarkFailed(result.ErrorMessage, result.Exception);
                WorkFlowMethodLog.Log("WorkflowEngine_ExecutionFailed",
                    new { workflowId = workflow.Id, error = result.ErrorMessage ?? "Unknown error", exception = result.Exception?.ToString() ?? "" });
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            session.MarkCancelled();
            WorkFlowMethodLog.Log("WorkflowEngine_ExecutionCancelled", new { workflowId = workflow.Id });
            return TConfig.ExecutionResult.Cancelled();
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex.Message, ex);
            WorkFlowMethodLog.Log("WorkflowEngine_ExecutionFailed",
                new { workflowId = workflow.Id, error = ex.Message, exception = ex.ToString() });
            return TConfig.ExecutionResult.Failure(ex.Message, ex);
        }
        finally
        {
            _ctsManager.TryRemove(session.SessionId);
        }
    }

    public Task CancelAsync(string workflowId)
    {
        var sessions = GetSessionsByWorkflowId(workflowId);
        foreach (var session in sessions)
        {
            if (session.Status == ExecutionFrameworkStatus.Running || session.Status == ExecutionFrameworkStatus.Paused)
            {
                _ctsManager.Cancel(session.SessionId);
            }
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync(string workflowId)
    {
        var sessions = GetSessionsByWorkflowId(workflowId);
        foreach (var session in sessions)
        {
            if (session.Status == ExecutionFrameworkStatus.Running)
            {
                session.MarkPaused();
                WorkFlowMethodLog.Log("WorkflowEngine_SessionPaused", new { workflowId, sessionId = session.SessionId });
            }
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string workflowId)
    {
        var sessions = GetSessionsByWorkflowId(workflowId);
        foreach (var session in sessions)
        {
            if (session.Status == ExecutionFrameworkStatus.Paused)
            {
                session.MarkResumed();
                WorkFlowMethodLog.Log("WorkflowEngine_SessionResumed", new { workflowId, sessionId = session.SessionId });
            }
        }
        return Task.CompletedTask;
    }

    public TConfig.ExecutionStatus GetStatus(string workflowId)
    {
        var sessions = GetSessionsByWorkflowId(workflowId);
        var session = sessions
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefault();
        return session?.Status ?? TConfig.ExecutionStatus.Pending;
    }

    TConfig.ExecutionStatus TConfig.IExecutionEngine.GetStatus(string planId)
    {
        return GetStatus(planId);
    }

    private List<IExecutionSession> GetSessionsByWorkflowId(string workflowId)
    {
        return _sessionManager.GetAllSessions()
            .Where(s => string.Equals(s.WorkflowId, workflowId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

