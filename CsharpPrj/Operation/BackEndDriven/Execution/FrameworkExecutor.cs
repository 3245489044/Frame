
using BackEndDriven.Diagnostics;
using BackEndDriven.Models;

namespace BackEndDriven.Execution;

public class FrameworkExecutor : IDisposable
{

    public Action? SetupGlobalExceptionHandling { get; set; }

    public Func<string[], object>? ParseArguments { get; set; }

    public Action<object>? ConnectPipe { get; set; }

    public Action<object>? SetupLogging { get; set; }

    public Action? SetWorkingDirectory { get; set; }

    public Action<object, bool>? InitializeFramework { get; set; }

    public Func<object, ExecutionResult>? ExecuteMode { get; set; }

    public Action<bool>? ShutdownFramework { get; set; }

    public bool IsCliMode { get; set; } = true;

    public Action? ShutdownLogging { get; set; }

    public Action? DisconnectPipe { get; set; }


    public FrameworkExecutor(Func<object, ExecutionResult>? executeMode = null)
    {
        ExecuteMode = executeMode;
    }

    /// <summary>
    /// AEF 装配（幂等）：为所有 FrameworkExecutor 宿主提供统一异步执行框架。
    /// 调度器 + 空闲看门狗；并发上限 4，空闲兜底 30s（单个操作可用 IdleTimeout&lt;=0 覆盖为不设时限）。
    /// </summary>
    private static void EnsureAefAssembled()
    {
        if (Modules.Threading.ThreadingBridge.AefScheduler != null) return;

        var scheduler = new Modules.Threading.AsyncOperationScheduler(
            maxConcurrency: 4,
            idleWindow: TimeSpan.FromSeconds(30));
        var watchdog = new Modules.Threading.WatchdogService();
        watchdog.Start(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        Modules.Threading.ThreadingBridge.InitializeAEF(scheduler, watchdog);
    }


    private bool _disposed;

    public ExecutionResult? LastResult { get; protected set; }


    public ExecutionResult Run(string[] args)
    {
        return RunInternal(args, CancellationToken.None);
    }

 
    public async Task<ExecutionResult> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() => RunInternal(args, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return LastResult ?? ExecutionResult.Failure("Execution was cancelled", "CLI");
        }
    }

    private ExecutionResult RunInternal(string[] args, CancellationToken cancellationToken)
    {
        void CheckCancel() => cancellationToken.ThrowIfCancellationRequested();

        CheckCancel();

        SetupGlobalExceptionHandling?.Invoke();

        if (ParseArguments == null)
        {
            LastResult = ExecutionResult.Failure("ParseArguments delegate is not configured", "CLI");
            return LastResult;
        }
        var options = ParseArguments.Invoke(args);
        if (options == null)
        {
            LastResult = ExecutionResult.EmptySuccess();
            return LastResult;
        }
        CheckCancel();

        ConnectPipe?.Invoke(options);

        bool loggingInitialized = false;
        if (SetupLogging != null)
        {
            SetupLogging.Invoke(options);
            loggingInitialized = true;
        }

        SetWorkingDirectory?.Invoke();

        bool frameworkInitialized = false;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        BackEndLog.Log("FrameworkExecutor_Start", new { mode = IsCliMode ? "CLI" : "GUI" });

        // AEF 装配（幂等）：统一异步执行框架——长任务收敛为 AsyncOp，经单一调度器流转
        // （绝对/空闲超时、取消贯穿、ActiveOps 可观测）。
        Modules.Threading.ThreadingBridge.Initialize();
        EnsureAefAssembled();

        try
        {
            InitializeFramework?.Invoke(options, IsCliMode);
            frameworkInitialized = true;
            CheckCancel();

            if (ExecuteMode != null)
            {
                LastResult = ExecuteMode.Invoke(options);
                if (LastResult == null)
                {
                    LastResult = ExecutionResult.Failure(
                        "ExecuteMode delegate returned null", IsCliMode ? "CLI" : "GUI");
                }
            }
            else
            {
                LastResult = ExecutionResult.EmptySuccess();
            }

            stopwatch.Stop();
            BackEndLog.Log("FrameworkExecutor_Complete", new
            {
                success = LastResult.Success,
                time = stopwatch.ElapsedMilliseconds
            });
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            LastResult = ExecutionResult.Failure("Execution was cancelled", "CLI");
            BackEndLog.Log("FrameworkExecutor_Error", new { error = "Execution was cancelled" });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LastResult = ExecutionResult.Failure(ex.Message);
            BackEndLog.Log("FrameworkExecutor_Error", new { error = ex.Message });
        }
        finally
        {
            if (frameworkInitialized)
            {
                ShutdownFramework?.Invoke(IsCliMode);
            }

            if (loggingInitialized)
            {
                ShutdownLogging?.Invoke();
            }

            DisconnectPipe?.Invoke();
        }

        return LastResult!;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            DisconnectPipe?.Invoke();
        }
        catch
        {
        }
    }
}
