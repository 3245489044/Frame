
using System.Diagnostics;
using Container.Abstractions;
using MediatR;
using static Container.Abstractions.NonEventCommandHelper;
using TConfig;
using ExecutionResult = Container.Abstractions.ExecutionResult;
using IFunctionExecutor = TConfig.IFunctionExecutor;
using IEventBindingResolver = TConfig.IEventBindingResolver;

namespace Container.Core;


public class ExecuteCommandHandler : IRequestHandler<ExecutionRequest, ExecutionResult>
{
    public async Task<ExecutionResult> Handle(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteCoreAsync(request, cancellationToken);
            sw.Stop();
            return result with { ExecutionTime = sw.Elapsed };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ExecutionResult.Fail(
                request.CommandText, "命令执行被取消") with { ExecutionTime = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.Fail(
                request.CommandText, ex.Message) with { ExecutionTime = sw.Elapsed };
        }
    }

    private Task<ExecutionResult> ExecuteCoreAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var commandText = request.CommandText;
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return Task.FromResult(
                ExecutionResult.Fail(commandText ?? "", "命令文本为空"));
        }


        var parts = commandText.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0] : "";
        var target = parts.Length > 1 ? parts[1] : "";
        var value = parts.Length > 2 ? parts[2] : "";

        if (string.IsNullOrWhiteSpace(action))
        {
            return Task.FromResult(
                ExecutionResult.Fail(commandText, "命令 action 为空"));
        }


        var runtimeCtx = request.RuntimeContext;

        try
        {
            var nonEventLookup = runtimeCtx?.GetService<INonEventCommandLookup>();

            if (NonEventCommandHelper.IsNonEventCommand(action, nonEventLookup?.Contains(action)))
            {
                return HandleNonEventCommandAsync(
                    commandText, action, target, value, runtimeCtx, cancellationToken);
            }
            else if (nonEventLookup == null)
            {
                return Task.FromResult(
                    ExecutionResult.Ok(commandText, value));
            }
            else
            {
                return HandleEventCommandAsync(
                    commandText, action, target, value, runtimeCtx, cancellationToken);
            }
        }

        catch (Exception ex)
        {
            return Task.FromResult(
                ExecutionResult.Fail(commandText, $"执行错误: {ex.Message}"));
        }
    }

    private static Task<ExecutionResult> HandleNonEventCommandAsync(
        string commandText,
        string action,
        string target,
        string value,
        IRuntimeContext? runtimeCtx,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nonEventLookup = runtimeCtx?.GetService<INonEventCommandLookup>();
        if (nonEventLookup == null)
        {
            return Task.FromResult(
                ExecutionResult.Ok(commandText, value));
        }

        if (!nonEventLookup.TryGetHandler(action, out var handler) || handler == null)
        {
            return Task.FromResult(
                ExecutionResult.Fail(commandText, $"未找到非事件命令 handler: {action}"));
        }

        try
        {
            var context = new NonEventExecutionContext
            {
                Action = action,
                Target = target,
                Value = value,
                CommandText = commandText,
                RuntimeContext = runtimeCtx
            };

            handler(context);

            return Task.FromResult(
                context.Success
                    ? ExecutionResult.Ok(commandText, context.ResultValue)
                    : ExecutionResult.Fail(commandText, context.ErrorMessage ?? "非事件命令执行失败"));
        }

        catch (Exception ex)
        {
            return Task.FromResult(
                ExecutionResult.Fail(commandText, $"非事件命令异常: {ex.Message}"));
        }
    }

    private static async Task<ExecutionResult> HandleEventCommandAsync(
        string commandText,
        string action,
        string target,
        string value,
        IRuntimeContext? runtimeCtx,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bindingResolver = runtimeCtx?.GetService<IEventBindingResolver>();
        var functionExecutor = runtimeCtx?.GetService<IFunctionExecutor>();

        if (bindingResolver == null || functionExecutor == null)
        {
            return ExecutionResult.Ok(commandText, value);
        }

        try
        {
            var eventConfig = new TConfig.EventConfig
            {
                EventsDictionary = new Dictionary<string, List<string>>
                {
                    [action] = new List<string> { target }
                }
            };
            var resolvedBindings = bindingResolver.Resolve(target, eventConfig);
            var resolvedBinding = resolvedBindings?.FirstOrDefault();

            if (resolvedBinding == null || string.IsNullOrWhiteSpace(resolvedBinding.FunctionName))
            {
                return ExecutionResult.Fail(commandText, $"未找到 {target}.{action} 的事件绑定");
            }

            var execResult = await functionExecutor.ExecuteAsync(resolvedBinding.FunctionName, new object?[] { value });
            var resultValue = execResult.IsSuccess ? execResult.Value?.ToString() : null;

            return ExecutionResult.Ok(commandText, resultValue) with
            {
                BoundFunctionName = resolvedBinding.FunctionName
            };
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(commandText, $"事件命令异常: {ex.Message}");
        }
    }
}


internal class NonEventExecutionContext : TConfig.IExecutionContext
{
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Value { get; set; }
    public string CommandText { get; set; } = "";
    public IRuntimeContext? RuntimeContext { get; set; }
    public bool Success { get; set; }
    public string? ResultValue { get; set; }
    public string? ErrorMessage { get; set; }

    public string ExecutionId => CommandText;
    public string WorkflowId => "";
    public string NodeKey => Action;
    public IReadOnlyDictionary<string, object> Parameters => new Dictionary<string, object>();
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();
    public CancellationToken CancellationToken { get; set; }

    private IServiceProvider? _serviceProvider;
    public IServiceProvider? ServiceProvider
    {
        get => _serviceProvider ?? RuntimeContext as IServiceProvider;
        set => _serviceProvider = value;
    }

    public T GetParameter<T>(string key) => default!;
    public T GetParameter<T>(int index) => default!;
    public bool TryGetParameter<T>(string key, out T value) { value = default!; return false; }
    public T GetData<T>(string key) => default!;
    public bool TryGetData<T>(string key, out T value) { value = default!; return false; }
    public void SetData<T>(string key, T value) { }
    public TConfig.IExecutionContext CreateSubContext(string? nodeKey = null) => this;
}



