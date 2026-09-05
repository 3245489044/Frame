using System.Text.Json;
using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Engine;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Systems;

public sealed class FunctionSystem : IIntentSystem
{
    private readonly TConfig.IFunctionExecutor? _functionExecutor;

    public FunctionSystem(TConfig.IFunctionExecutor? functionExecutor = null)
    {
        _functionExecutor = functionExecutor;
    }

    public string StepType => "Function";

    public async Task<IntentResult> ExecuteAsync(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var funcRef = entity.FuncRef;
        if (_functionExecutor == null)
            return IntentResult.Failure("FunctionSystem 未注入 IFunctionExecutor，无法执行函数");
        if (string.IsNullOrEmpty(funcRef))
            return IntentResult.Failure($"Entity '{entity.Name}' 的 FuncRef 为空，无法执行函数");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var policy = entity.Policy;
            var attempts = Math.Max(1, (policy?.RetryCount ?? 0) + 1);
            var retryDelay = policy?.RetryDelayMs ?? 1000;
            var timeoutMs = policy?.TimeoutMs ?? 0;

            using var timeoutCts = timeoutMs > 0 ? new CancellationTokenSource() : null;
            if (timeoutCts != null)
                timeoutCts.CancelAfter(timeoutMs);

            TConfig.ExecutionResult? result = null;

            // 取消贯穿（P3）：把框架 ct 作为 ExecutionContext.CancellationToken 传入执行器，
            // 使其能链到内层 cts 并经 CancellationTokenInjector 注入给声明 ct 的函数（如 ClineClient）。
            var execContext = cancellationToken != CancellationToken.None
                ? new TConfig.ExecutionContext { CancellationToken = cancellationToken }
                : null;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                result = await _functionExecutor.ExecuteAsync(
                    funcRef, resolvedParameters.ToNonNullDictionary(), execContext);

                if (result.IsSuccess)
                    break;

                if (attempt < attempts - 1 && retryDelay > 0)
                    await Task.Delay(retryDelay, cancellationToken);
            }

            if (result == null)
                return IntentResult.Failure("函数执行器返回空结果");

            if (!result.IsSuccess)
                return IntentResult.Failure(result.ErrorMessage ?? $"函数 '{funcRef}' 执行失败");

            var outputKey = entity.OutputKey;

            var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(outputKey))
                outputs[outputKey] = result.Value;

            if (result.Value is Dictionary<string, object> resultDict)
            {
                foreach (var kvp in resultDict)
                    outputs[kvp.Key] = kvp.Value;
            }

            return IntentResult.Success(result.Value, outputs);
        }
        catch (OperationCanceledException)
        {
            return IntentResult.Failure("执行已取消");
        }
        catch (Exception ex)
        {
            return IntentResult.Failure($"函数 '{funcRef}' 执行异常: {ex.Message}");
        }
    }
}