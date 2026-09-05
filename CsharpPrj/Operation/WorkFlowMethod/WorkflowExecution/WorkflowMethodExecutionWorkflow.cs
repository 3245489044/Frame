
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandFramework.Abstractions;
using TConfig;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Parameters;

namespace WorkFlowMethod.Execution
{
  public class WorkflowMethodExecutionWorkflow : IParameterExecutionWorkflow
    {
        private readonly IEventExecutionService _executionService;
        private readonly TConfig.Controls.IControlValueAccessor _controlValueAccessor;
        private readonly FrameworkAbstractions.WinFormsAbstractions.IControlFinder? _controlFinder;

        public WorkflowMethodExecutionWorkflow(
            IEventExecutionService executionService,
            TConfig.Controls.IControlValueAccessor controlValueAccessor,
            FrameworkAbstractions.WinFormsAbstractions.IControlFinder? controlFinder = null)
        {
            _executionService = executionService
                ?? throw new ArgumentNullException(nameof(executionService));
            _controlValueAccessor = controlValueAccessor
                ?? throw new ArgumentNullException(nameof(controlValueAccessor));
            _controlFinder = controlFinder;
        }

        public async Task<ParameterExecutionResult> ExecuteAsync(
            ParameterExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var functionName = request.FunctionName;

            WorkFlowMethodLog.Log("WorkflowMethodExecution_Start", new
            {
                functionName,
                inputControls = request.InputControls ?? "",
                isGuiMode = request.IsGuiMode
            });

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var conversionResult = await ConvertControlValuesToParametersAsync(
                    request, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                var execResult = await _executionService.ExecuteFunctionAsync(
                    functionName, conversionResult.Parameters);

                if (!execResult.IsSuccess)
                {
                    var errorMsg = execResult.ErrorMessage ?? "Unknown error";
                    throw new InvalidOperationException(
                        $"Function '{functionName}' failed: {errorMsg}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                WriteResultsBack(request, conversionResult, execResult);

                var executionTime = DateTime.UtcNow - startTime;

                WorkFlowMethodLog.Log("WorkflowMethodExecution_Complete", new
                {
                    functionName,
                    time = executionTime.TotalMilliseconds
                });

                return ParameterExecutionResult.Ok(execResult.Value, executionTime);
            }
            catch (OperationCanceledException)
            {
                WorkFlowMethodLog.Log("WorkflowMethodExecution_Cancelled", new { functionName });
                return ParameterExecutionResult.Fail("Parameter execution was cancelled.");
            }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Log("WorkflowMethodExecution_Error", new
                {
                    functionName,
                    error = ex.Message
                });
                return ParameterExecutionResult.Fail(
                    $"WorkflowMethod execution error: {ex.Message}");
            }
        }

        private async Task<ParameterConversionResult> ConvertControlValuesToParametersAsync(
            ParameterExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var result = new ParameterConversionResult();

            var controlNames = string.IsNullOrEmpty(request.InputControls)
                ? Array.Empty<string>()
                : CommandFramework.Parsing.StringUtils.SplitEscapedToArray(request.InputControls);

            foreach (var ctrl in controlNames)
            {
                string? rawValue = null;
                if (request.IsGuiMode && request.TargetForm != null)
                {
                    var finder = request.ControlFinder as FrameworkAbstractions.WinFormsAbstractions.IControlFinder ?? _controlFinder;
                    if (finder != null)
                    {
                        var control = finder.FindByName<System.Windows.Forms.Control>(ctrl);
                        if (control == null)
                        {
                            throw new InvalidOperationException(
                                $"WorkflowMethodExecutionWorkflow: 在 GUI 模式下找不到控件 '{ctrl}'。"
                                + "请确认控件名与 JSON 配置中的 inputControls 一致。"
                                + $"FunctionName='{request.FunctionName}'");
                        }
                        rawValue = _controlValueAccessor.GetValue(control)?.ToString();
                    }
                }
                else
                {
                    if (request.VirtualStore != null)
                        request.VirtualStore.TryGetValue(ctrl, out rawValue);
                }

                result.RawControlValues[ctrl] = rawValue ?? string.Empty;
            }

            foreach (var ctrl in controlNames)
            {
                if (result.RawControlValues.TryGetValue(ctrl, out var rawVal))
                {
                    object paramValue;
                    if (request.ParameterTypes != null &&
                        request.ParameterTypes.TryGetValue(ctrl, out var targetType) &&
                        targetType != typeof(string))
                    {
                        try
                        {
                            paramValue = TypeConverter.ConvertTo(rawVal, targetType);
                        }
                        catch
                        {
                            paramValue = rawVal ?? string.Empty;
                            WorkFlowMethodLog.Log("WorkflowMethodExecution_TypeConversionFallback",
                                new { parameter = ctrl, targetType = targetType.Name, value = rawVal });
                        }
                    }
                    else
                    {
                        paramValue = rawVal ?? string.Empty;
                    }

                    result.Parameters[ctrl] = paramValue;
                }
            }

            return result;
        }

        private void WriteResultsBack(
            ParameterExecutionRequest request,
            ParameterConversionResult conversion,
            ExecutionResult execResult)
        {
            if (string.IsNullOrEmpty(request.OutputTarget))
                return;

            if (execResult.Value != null)
            {
                var valueStr = TypeConverter.FormatValue(execResult.Value);
                WriteToTarget(request, request.OutputTarget, valueStr);
            }

        }

        private void WriteToTarget(
            ParameterExecutionRequest request,
            string target,
            string? value)
        {
            if (request.IsGuiMode && request.TargetForm != null)
            {
                var finder = request.ControlFinder as FrameworkAbstractions.WinFormsAbstractions.IControlFinder ?? _controlFinder;
                if (finder == null)
                {
                    WorkFlowMethodLog.Log("WorkflowMethodExecution_WriteResult_NoFinder", new
                    {
                        target, value
                    });
                    return;
                }

                var control = finder.FindByName<System.Windows.Forms.Control>(target);
                if (control != null)
                {
                    _controlValueAccessor.SetValue(control, value);
                    WorkFlowMethodLog.Log("WorkflowMethodExecution_WriteResult_Ok", new
                    {
                        target, value,
                        writeTarget = "control"
                    });
                }
                else
                {
                    WorkFlowMethodLog.Log("WorkflowMethodExecution_WriteResult_ControlNotFound", new
                    {
                        target, value
                    });
                }
            }
            else if (request.VirtualStore != null)
            {
                request.VirtualStore[target] = value;
                WorkFlowMethodLog.Log("WorkflowMethodExecution_WriteResult_Ok", new
                {
                    target, value,
                    writeTarget = "virtualStore"
                });
            }
            else
            {
                WorkFlowMethodLog.Log("WorkflowMethodExecution_WriteResult_NoTarget", new
                {
                    target, value,
                    isGuiMode = request.IsGuiMode,
                    virtualStoreNull = request.VirtualStore == null
                });
            }
        }
    }

    public class ParameterConversionResult
    {
        public Dictionary<string, object> Parameters { get; set; } = new();

        public Dictionary<string, string?> RawControlValues { get; set; } = new();
    }

    public static class TypeConverter
    {
        public static object? ConvertTo(string? value, Type targetType)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                if (string.IsNullOrEmpty(value))
                    return null;
                targetType = underlyingType;
            }

            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType == typeof(string)) return value;
            if (targetType.IsEnum) return Enum.Parse(targetType, value);

            return ParameterTypeConverter.Default.ConvertTo(value, targetType.Name);
        }

        public static string FormatValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                string s => s,
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                bool b => b.ToString(),
                _ => System.Text.Json.JsonSerializer.Serialize(value)
            };
        }
    }
}
