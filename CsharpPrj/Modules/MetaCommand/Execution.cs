using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.StateMachine;
using Modules.Parsing;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Adapters;
using Modules.Parsing.Models;
using Modules.Parsing.Parsing;
using MetaCommandModel = Modules.Parsing.Models.MetaCommand;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaCommand
{
    #region 中间件管线

    public class MiddlewarePipeline
    {
        public enum PipelineOp { Use, Build, Clear }

        public const int UNBUILT = 0;
        public const int BUILT = 1;

        private readonly List<ICommandMiddleware> _middlewares = new();
        private CommandDelegate? _finalHandler;
        private CommandDelegate? _builtPipeline;
        private readonly object _buildLock = new();
        private readonly LifecycleStateMachine<PipelineOp> _stateMachine;

        public MiddlewarePipeline()
        {
            _stateMachine = new LifecycleStateMachine<PipelineOp>(UNBUILT);
            _stateMachine.Permit(UNBUILT, PipelineOp.Use,   UNBUILT);
            _stateMachine.Permit(UNBUILT, PipelineOp.Build, BUILT);
            _stateMachine.Permit(BUILT,   PipelineOp.Clear, UNBUILT);
        }

        public int CurrentState => _stateMachine.CurrentState;

        public MiddlewarePipeline Use(ICommandMiddleware middleware)
        {
            _stateMachine.Fire(PipelineOp.Use);
            lock (_buildLock)
            {
                _middlewares.Add(middleware);
                _builtPipeline = null;
            }
            return this;
        }

        public MiddlewarePipeline Use(Func<MetaCommandModel, CommandDelegate, CancellationToken, Task<OperationResult<object>>> middlewareFunc)
        {
            _stateMachine.Fire(PipelineOp.Use);
            lock (_buildLock)
            {
                _middlewares.Add(new DelegateMiddleware(middlewareFunc));
                _builtPipeline = null;
            }
            return this;
        }

        public MiddlewarePipeline Use(Func<MetaCommandModel, CancellationToken, Task<OperationResult<object>>> handler)
        {
            _stateMachine.Fire(PipelineOp.Use);
            lock (_buildLock)
            {
                _finalHandler = (cmd, ct) => handler(cmd, ct);
                _builtPipeline = null;
            }
            return this;
        }

        public CommandDelegate Build()
        {
            if (_builtPipeline != null)
                return _builtPipeline;

            _stateMachine.Fire(PipelineOp.Build);

            lock (_buildLock)
            {
                if (_builtPipeline != null)
                    return _builtPipeline;

                if (_finalHandler == null)
                    throw new InvalidOperationException("Pipeline must have a final handler");

                CommandDelegate current = _finalHandler;

                foreach (var middleware in _middlewares.AsEnumerable().Reverse())
                {
                    var next = current;
                    current = (command, ct) => middleware.InvokeAsync(command, next, ct);
                }

                _builtPipeline = current;
            }

            return _builtPipeline;
        }

        public void Clear()
        {
            _stateMachine.Fire(PipelineOp.Clear);
            lock (_buildLock)
            {
                _middlewares.Clear();
                _finalHandler = null;
                _builtPipeline = null;
            }
        }

        public int MiddlewareCount
        {
            get { lock (_buildLock) { return _middlewares.Count; } }
        }

        private class DelegateMiddleware : ICommandMiddleware
        {
            private readonly Func<MetaCommandModel, CommandDelegate, CancellationToken, Task<OperationResult<object>>> _middlewareFunc;

            public DelegateMiddleware(Func<MetaCommandModel, CommandDelegate, CancellationToken, Task<OperationResult<object>>> middlewareFunc)
            {
                _middlewareFunc = middlewareFunc;
            }

            public Task<OperationResult<object>> InvokeAsync(MetaCommandModel command,
                                                               CommandDelegate next,
                                                               CancellationToken cancellationToken)
            {
                return _middlewareFunc(command, next, cancellationToken);
            }
        }
    }

    #endregion

    #region 中间件实现

    public class AuthorizationMiddleware : ICommandMiddleware
    {
        private readonly IPermissionService _permissionService;
        private readonly ILogger<AuthorizationMiddleware> _logger;

        public AuthorizationMiddleware(IPermissionService permissionService, ILogger<AuthorizationMiddleware> logger)
        {
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationResult<object>> InvokeAsync(MetaCommandModel command,
                                                               CommandDelegate next,
                                                               CancellationToken cancellationToken)
        {
            var permissionResult = await _permissionService.CheckPermissionAsync(command);

            if (!permissionResult.Success)
            {
                _logger.LogWarning("Permission denied for command: {Verb} {Action} {Target}",
                    command.Verb, command.Action, command.Target);
                return OperationResult<object>.FailureResult(permissionResult.Message,
                    ErrorCode.PermissionDenied, commandId: command.CommandId);
            }

            _logger.LogDebug("Permission granted for command: {Verb} {Action} {Target}",
                command.Verb, command.Action, command.Target);

            return await next(command, cancellationToken);
        }
    }

    public class ErrorHandlingMiddleware : ICommandMiddleware
    {
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationResult<object>> InvokeAsync(MetaCommandModel command,
                                                               CommandDelegate next,
                                                               CancellationToken cancellationToken)
        {
            try
            {
                return await next(command, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("Command canceled: {CommandId}", command.CommandId);
                return OperationResult<object>.FailureResult("Command was canceled",
                    ErrorCode.OperationCanceled, ex, commandId: command.CommandId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error executing command: {CommandId}", command.CommandId);
                return OperationResult<object>.FailureResult($"Unexpected error: {ex.Message}",
                    ErrorCode.UnknownError, ex, commandId: command.CommandId);
            }
        }
    }

    public class LoggingMiddleware : ICommandMiddleware
    {
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationResult<object>> InvokeAsync(MetaCommandModel command,
                                                               CommandDelegate next,
                                                               CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing command: {Verb} {Action} {Target}",
                command.Verb, command.Action, command.Target);

            try
            {
                var result = await next(command, cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation("Command succeeded: {CommandId}", command.CommandId);
                }
                else
                {
                    _logger.LogWarning("Command failed: {CommandId}, Error: {Message}",
                        command.CommandId, result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command execution exception: {CommandId}", command.CommandId);
                return OperationResult<object>.FailureResult($"Command execution exception: {ex.Message}",
                    ErrorCode.ExecutionFailed, ex, commandId: command.CommandId);
            }
        }
    }

    public class PerformanceMiddleware : ICommandMiddleware
    {
        private readonly ILogger<PerformanceMiddleware> _logger;
        private readonly ICommandStatistics _statistics;

        public PerformanceMiddleware(ILogger<PerformanceMiddleware> logger, ICommandStatistics statistics)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        }

        public async Task<OperationResult<object>> InvokeAsync(MetaCommandModel command,
                                                               CommandDelegate next,
                                                               CancellationToken cancellationToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool success = false;

            try
            {
                var result = await next(command, cancellationToken);
                success = result.Success;
                return result;
            }
            finally
            {
                stopwatch.Stop();

                _statistics.RecordExecution(command.Verb.ToString(), command.Action, success, stopwatch.Elapsed);

                if (stopwatch.Elapsed.TotalMilliseconds > 1000)
                {
                    _logger.LogWarning("Slow command execution: {Verb} {Action} took {Duration}ms",
                        command.Verb, command.Action, stopwatch.Elapsed.TotalMilliseconds);
                }
                else
                {
                    _logger.LogDebug("Command execution time: {Duration}ms", stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }
    }

    #endregion

    #region 命令路由器

    public class CommandRouter : ICommandRouter
    {
        public enum RouterOp { Register, Unregister, SetActive }

        public const int EMPTY = 0;
        public const int READY = 1;

        private readonly ConcurrentDictionary<string, ICommandExecutor> _executors = new();
        private string? _activeExecutorId;

        private readonly ILogger<CommandRouter> _logger;
        private readonly ICommandStatistics _statistics;
        private readonly MiddlewarePipeline? _middlewarePipeline;
        private readonly IPermissionService? _permissionService;
        private readonly MetaCommandOptions? _options;
        private readonly Lazy<CommandDelegate> _pipelineDelegate;
        private readonly LifecycleStateMachine<RouterOp> _stateMachine;

        public CommandRouter(ILogger<CommandRouter> logger,
                           ICommandStatistics statistics,
                           IPermissionService? permissionService = null,
                           MiddlewarePipeline? middlewarePipeline = null,
                           MetaCommandOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _statistics.Start();
            _permissionService = permissionService;
            _middlewarePipeline = middlewarePipeline;
            _options = options;

            _stateMachine = new LifecycleStateMachine<RouterOp>(EMPTY);
            _stateMachine.Permit(EMPTY, RouterOp.Register,   READY);
            _stateMachine.Permit(READY, RouterOp.Register,   READY);
            _stateMachine.Permit(READY, RouterOp.Unregister, READY);
            _stateMachine.Permit(READY, RouterOp.Unregister, EMPTY);
            _stateMachine.Permit(READY, RouterOp.SetActive,  READY);

            _pipelineDelegate = new Lazy<CommandDelegate>(InitializePipeline, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public int CurrentState => _stateMachine.CurrentState;

        private CommandDelegate InitializePipeline()
        {
            if (_options != null && !_options.EnableMiddlewarePipeline)
                return null!;

            if (_middlewarePipeline == null)
                return null!;

            if (_middlewarePipeline.MiddlewareCount > 0)
                return _middlewarePipeline.Build();

            ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

            _middlewarePipeline
                .Use(new ErrorHandlingMiddleware(loggerFactory.CreateLogger<ErrorHandlingMiddleware>()))
                .Use(new PerformanceMiddleware(loggerFactory.CreateLogger<PerformanceMiddleware>(), _statistics))
                .Use(new LoggingMiddleware(loggerFactory.CreateLogger<LoggingMiddleware>()));

            if (_permissionService != null && _options is { EnableAuthorization: true })
            {
                _middlewarePipeline.Use(new AuthorizationMiddleware(_permissionService, loggerFactory.CreateLogger<AuthorizationMiddleware>()));
            }

            _middlewarePipeline.Use(async (cmd, ct) => await ExecuteMetaCommandAsync(cmd, ct));

            return _middlewarePipeline.Build();
        }

        public async Task<OperationResult<object>> ExecuteCommandAsync(string command,
                                                                          CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(command))
                return OperationResult<object>.FailureResult("Command cannot be null or empty",
                    ErrorCode.CommandEmpty);

            try
            {
                var MetaCommandModel = CommandParser.Parse(command);

                if (MetaCommandModel == null)
                    return OperationResult<object>.SuccessResult(null, "Comment or empty command, skipped");

                var validationResult = MetaCommandModel.ValidateCommand();
                if (!validationResult.IsValid)
                {
                    var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                    _statistics.RecordError("validation", ErrorCode.InvalidParameter);
                    return OperationResult<object>.FailureResult($"Validation failed: {errors}",
                        ErrorCode.InvalidParameter);
                }

                var pipeline = _pipelineDelegate.Value;
                if (pipeline != null)
                {
                    return await pipeline(MetaCommandModel, cancellationToken);
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await ExecuteMetaCommandAsync(MetaCommandModel, cancellationToken);
                stopwatch.Stop();
                _statistics.RecordExecution(MetaCommandModel.Verb.ToString(), MetaCommandModel.Action, result.Success, stopwatch.Elapsed);
                return result;
            }
            catch (ArgumentException ex)
            {
                _statistics.RecordError("parse", ErrorCode.CommandFormatError);
                return OperationResult<object>.FailureResult($"Command format error: {ex.Message}",
                    ErrorCode.CommandFormatError, ex);
            }
            catch (FormatException ex)
            {
                _statistics.RecordError("parse", ErrorCode.CommandFormatError);
                return OperationResult<object>.FailureResult($"Command format error: {ex.Message}",
                    ErrorCode.CommandFormatError, ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error executing command");
                _statistics.RecordError("execute", ErrorCode.UnknownError);
                return OperationResult<object>.FailureResult($"Unexpected error: {ex.Message}",
                    ErrorCode.UnknownError, ex);
            }
        }

        public async Task<OperationResult<object>> ExecuteMetaCommandAsync(MetaCommandModel command,
                                                                             CancellationToken cancellationToken = default)
        {
            var executor = GetActiveExecutor();
            if (executor == null)
            {
                return OperationResult<object>.FailureResult("No active executor registered",
                    ErrorCode.NoActiveAdapter);
            }

            _logger.LogDebug("Executing command: {Verb} {Action} {Target}",
                command.Verb, command.Action, command.Target);

            switch (command.Verb)
            {
                case MetaVerb.Do:
                    return (await executor.DoAsync(command.Action, command.Target,
                        command.Parameters, cancellationToken)).ToObjectResult();

                case MetaVerb.Get:
                    return (await executor.GetAsync(command.Target, command.Parameters, cancellationToken)).ToObjectResult();

                case MetaVerb.Look:
                    return (await executor.LookAsync(command.Target, command.Parameters, cancellationToken)).ToObjectResult();

                case MetaVerb.Wait:
                    var waitCondition = command.Target
                        ?? command.Parameters?.GetValueOrDefault("condition")?.ToString()
                        ?? string.Empty;
                    var waitTimeoutStr = command.Parameters?.GetValueOrDefault("timeoutMs")?.ToString();
                    var defaultTimeout = _options?.DefaultTimeoutMs ?? 30000;
                    if (!int.TryParse(waitTimeoutStr, out var timeoutMs) || timeoutMs <= 0)
                        timeoutMs = defaultTimeout;
                    return (await executor.WaitAsync(waitCondition, timeoutMs, cancellationToken)).ToObjectResult();

                case MetaVerb.Assert:
                    var assertPredicate = command.Target ?? string.Empty;
                    return (await executor.AssertAsync(assertPredicate, command.Parameters, cancellationToken)).ToObjectResult();

                default:
                    return OperationResult<object>.FailureResult($"Unknown verb: {command.Verb}",
                        ErrorCode.InvalidParameter);
            }
        }

        public void RegisterExecutor(string executorId, ICommandExecutor executor)
        {
            if (string.IsNullOrWhiteSpace(executorId))
                throw new ArgumentNullException(nameof(executorId));
            if (executor == null)
                throw new ArgumentNullException(nameof(executor));

            _stateMachine.Fire(RouterOp.Register);

            _executors[executorId] = executor;

            if (_activeExecutorId == null)
            {
                _activeExecutorId = executorId;
                _logger.LogInformation("Registered and activated executor: {ExecutorId}", executorId);
            }
            else
            {
                _logger.LogInformation("Registered executor: {ExecutorId}", executorId);
            }
        }

        public void UnregisterExecutor(string executorId)
        {
            if (string.IsNullOrWhiteSpace(executorId))
                throw new ArgumentNullException(nameof(executorId));

            _stateMachine.Fire(RouterOp.Unregister);

            _executors.TryRemove(executorId, out _);

            if (_activeExecutorId == executorId)
            {
                _activeExecutorId = _executors.Keys.FirstOrDefault();
                _logger.LogInformation("Unregistered executor: {ExecutorId}, active executor changed to: {NewExecutorId}",
                    executorId, _activeExecutorId ?? "none");
            }
            else
            {
                _logger.LogInformation("Unregistered executor: {ExecutorId}", executorId);
            }

            if (_executors.IsEmpty && _stateMachine.CurrentState == READY)
            {
                _stateMachine.Fire(RouterOp.Unregister);
            }
        }

        public string? GetActiveExecutorId() => _activeExecutorId;

        public void SetActiveExecutor(string executorId)
        {
            if (string.IsNullOrWhiteSpace(executorId))
                throw new ArgumentNullException(nameof(executorId));

            if (!_executors.ContainsKey(executorId))
                throw new ArgumentException($"Executor not found: {executorId}");

            _stateMachine.Fire(RouterOp.SetActive);

            _activeExecutorId = executorId;
            _logger.LogInformation("Active executor changed to: {ExecutorId}", executorId);
        }

        public IEnumerable<string> GetRegisteredExecutors()
        {
            return _executors.Keys.ToList();
        }

        public bool TryGetExecutor(string executorId, out ICommandExecutor? executor)
        {
            return _executors.TryGetValue(executorId, out executor);
        }

        public CommandMetrics GetMetrics() => _statistics.GetMetrics();

        public void ResetMetrics() => _statistics.Reset();

        private ICommandExecutor? GetActiveExecutor()
        {
            if (_activeExecutorId == null)
                return null;

            _executors.TryGetValue(_activeExecutorId, out var executor);
            return executor;
        }
    }

    #endregion
}
