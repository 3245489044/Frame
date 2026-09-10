
using System.Diagnostics;
using CommandFramework.Models;
using Container.Abstractions;
using Microsoft.Extensions.Logging;


namespace CommandFramework.Invoke;


public class ContainerExecutionCell : IExecutionCell
{
    private readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;
    private readonly INonEventCommandLookup? _nonEventCommandLookup;


    private readonly ILogger? _logger;
    private readonly string _name;

    public string Name => _name;

    public ContainerExecutionCell(
        IParameterExecutionWorkflow? parameterExecutionWorkflow = null,
        INonEventCommandLookup? nonEventCommandLookup = null,
        ILogger? logger = null,
        string? name = null)

    {
        _parameterExecutionWorkflow = parameterExecutionWorkflow;
        _nonEventCommandLookup = nonEventCommandLookup;
        _logger = logger;
        _name = name ?? "ContainerExecutionCell";
    }

    public async Task<ContainerExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var commandText = request.CommandText;
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return ContainerExecutionResult.Fail(commandText ?? "", "命令文本为空");
            }

            var parts = commandText.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var action = parts.Length > 0 ? parts[0] : "";
            var target = parts.Length > 1 ? parts[1] : "";
            var value = parts.Length > 2 ? parts[2] : "";

            if (string.IsNullOrWhiteSpace(action))
            {
                return ContainerExecutionResult.Fail(commandText, "命令 action 为空");
            }

            if (NonEventCommandHelper.IsNonEventCommand(action, _nonEventCommandLookup?.Contains(action)))
            {
                return await HandleNonEventCommandAsync(
                    commandText, action, target, value, cancellationToken);
            }
            else
            {
                return await HandleEventCommandAsync(
                    commandText, action, target, value, request, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ContainerExecutionResult.Fail(
                request.CommandText, "命令执行被取消") with { ExecutionTime = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogError(ex, "ContainerExecutionCell 执行异常: {Command}", request.CommandText);
            return ContainerExecutionResult.Fail(
                request.CommandText, $"执行异常: {ex.Message}") with { ExecutionTime = sw.Elapsed };
        }
    }

    private async Task<ContainerExecutionResult> HandleEventCommandAsync(
        string commandText,
        string action,
        string target,
        string value,
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (_parameterExecutionWorkflow == null)
        {
            return ContainerExecutionResult.Fail(commandText,
                "参数执行工作流未配置，无法执行事件命令。请确保 IParameterExecutionWorkflow 已注入。");
        }

        try
        {
            var runtimeCtx = request.RuntimeContext;
            var isGuiMode = request.Mode == ExecutionMode.Gui;

            var execRequest = new ParameterExecutionRequest
            {
                FunctionName = target,
                InputControls = value,
                OutputTarget = null,
                IsGuiMode = isGuiMode,
                TargetForm = (runtimeCtx?.Items.TryGetValue("TargetForm", out var form) == true) ? form : null,
                VirtualStore = null,
                ControlFinder = runtimeCtx?.GetService<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>(),
                ControlValueWriter = runtimeCtx?.GetService<TConfig.Controls.IControlValueAccessor>()
            };

            var result = await _parameterExecutionWorkflow.ExecuteAsync(execRequest, cancellationToken);

            if (result.Success)
            {
                return ContainerExecutionResult.Ok(commandText, result.Value?.ToString()) with
                {
                    ExecutionTime = result.ExecutionTime
                };
            }
            else
            {
                return ContainerExecutionResult.Fail(commandText, result.ErrorMessage ?? "事件命令执行失败");
            }
        }
        catch (Exception ex)
        {
            return ContainerExecutionResult.Fail(commandText, $"事件命令异常: {ex.Message}");
        }
    }

    private Task<ContainerExecutionResult> HandleNonEventCommandAsync(
        string commandText,
        string action,
        string target,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_nonEventCommandLookup == null)
        {
            return Task.FromResult(
                ContainerExecutionResult.Fail(commandText, "非事件命令查找器未配置"));
        }

        try
        {
            var context = new CommandContext
            {
                CurrentCommand = commandText,
                VirtualStore = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            };
            context.Mode = "CLI";

            if (_nonEventCommandLookup.TryGetHandler(action, out var handler) && handler != null)
            {
                handler(context);

                return Task.FromResult(
                    context.GetFunctionExecuteSuccess()
                        ? ContainerExecutionResult.Ok(commandText, context.FunctionResultValue?.ToString())
                        : ContainerExecutionResult.Fail(commandText, context.GetFunctionErrorMessage() ?? "非事件命令执行失败"));
            }

            return Task.FromResult(
                ContainerExecutionResult.Fail(commandText, $"未找到非事件命令 handler: {action}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                ContainerExecutionResult.Fail(commandText, $"非事件命令异常: {ex.Message}"));
        }
    }
}
