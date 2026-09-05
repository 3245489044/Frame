
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace MetaCommand
{
    public class ConfigExecutor : BaseCommandExecutor
    {
        private readonly ConcurrentDictionary<string, object?> _configStore = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

        protected int DefaultTimeoutMs { get; set; } = 30000;

        public ConfigExecutor()
            : base("config", "Config Executor", "1.0") { }


        public override Task<OperationResult<object>> DoAsync(
            string action, string? target, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(target))
                return Task.FromResult(OperationResult<object>.FailureResult("Target is required", ErrorCode.InvalidParameter));

            try
            {
                var value = parameters?.GetValueOrDefault("value")?.ToString();
                var oldValue = _configStore.GetOrAdd(target, _ => null);
                _configStore[target] = value;

                OnConfigChanged(new ConfigChangedEventArgs(target, oldValue?.ToString(), value));

                return Task.FromResult(OperationResult<object>.SuccessResult(value,
                    $"已设置配置 {target} = {value}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<object>.FailureResult(
                    $"设置配置失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override Task<OperationResult<object>> GetAsync(
            string entity, Dictionary<string, object>? queryParams, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entity))
                return Task.FromResult(OperationResult<object>.FailureResult("Entity is required", ErrorCode.InvalidParameter));

            try
            {
                if (_configStore.TryGetValue(entity, out var value))
                {
                    return Task.FromResult(OperationResult<object>.SuccessResult(value,
                        $"已读取 {entity}"));
                }
                return Task.FromResult(OperationResult<object>.FailureResult(
                    $"配置项不存在: {entity}", ErrorCode.TargetNotFound));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<object>.FailureResult(
                    $"读取配置失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override Task<OperationResult<WorldModel>> LookAsync(
            string? scope, Dictionary<string, object>? options, CancellationToken ct = default)
        {
            try
            {
                var matchingItems = _configStore
                    .Where(kvp => string.IsNullOrEmpty(scope) || kvp.Key.StartsWith(scope, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kvp => kvp.Key)
                    .ToList();

                var model = new WorldModel
                {
                    AdapterId = ExecutorId,
                    Scope = scope ?? "all",
                    Timestamp = DateTime.UtcNow
                };

                foreach (var kvp in matchingItems)
                {
                    model.Entities.Add(new EntityDescriptor
                    {
                        Name = kvp.Key,
                        Type = "Config",
                        Properties = new Dictionary<string, object>
                        {
                            ["Value"] = kvp.Value?.ToString() ?? "(null)"
                        }
                    });
                }

                return Task.FromResult(OperationResult<WorldModel>.SuccessResult(model,
                    $"已列出 {matchingItems.Count} 个配置项"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<WorldModel>.FailureResult(
                    $"列出配置失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override async Task<OperationResult<bool>> WaitAsync(
            string condition, int timeoutMs, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return OperationResult<bool>.FailureResult("Condition is required", ErrorCode.InvalidParameter);

            try
            {
                var expectedValue = condition;
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs);
                using var registration = cts.Token.Register(() => tcs.TrySetResult(false));

                EventHandler<ConfigChangedEventArgs> handler = (s, e) =>
                {
                    if (!string.Equals(e.Key, condition, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (expectedValue == null || string.Equals(e.NewValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        tcs.TrySetResult(true);
                    }
                };

                ConfigChanged += handler;

                try
                {
                    var result = await tcs.Task;
                    return OperationResult<bool>.SuccessResult(result,
                        result ? "配置已变更" : "等待超时");
                }
                finally
                {
                    ConfigChanged -= handler;
                }
            }
            catch (Exception ex)
            {
                return OperationResult<bool>.FailureResult($"等待配置变更失败: {ex.Message}", ErrorCode.ExecutionFailed);
            }
        }


        public override Task<OperationResult<bool>> AssertAsync(
            string predicate, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(predicate))
                return Task.FromResult(OperationResult<bool>.FailureResult("Predicate is required", ErrorCode.InvalidParameter));

            try
            {
                var expectedValue = parameters?.GetValueOrDefault("expected")?.ToString();

                if (!_configStore.TryGetValue(predicate, out var actualValue))
                {
                    return Task.FromResult(OperationResult<bool>.FailureResult(
                        $"配置项不存在: {predicate}", ErrorCode.TargetNotFound));
                }

                var actualStr = actualValue?.ToString();
                var isMatch = string.Equals(actualStr, expectedValue, StringComparison.OrdinalIgnoreCase);

                return Task.FromResult(OperationResult<bool>.SuccessResult(isMatch,
                    isMatch ? $"断言通过: {predicate} = {actualStr}"
                            : $"断言失败: 期望 '{expectedValue}', 实际 '{actualStr}'"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<bool>.FailureResult(
                    $"断言失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override async Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(
            string? scope, CancellationToken ct = default)
        {
            var model = await LookAsync(scope, null, ct);
            if (!model.Success)
                return OperationResult<List<EntityDescriptor>>.FailureResult(model.Message, model.ErrorCode);

            return OperationResult<List<EntityDescriptor>>.SuccessResult(
                model.Data?.Entities ?? new List<EntityDescriptor>(),
                $"已发现 {(model.Data?.Entities?.Count ?? 0)} 个配置项");
        }


        protected virtual void OnConfigChanged(ConfigChangedEventArgs e)
        {
            ConfigChanged?.Invoke(this, e);
        }

        public void Clear()
        {
            _configStore.Clear();
        }

        public void SetRange(IEnumerable<KeyValuePair<string, object?>> configs)
        {
            foreach (var kvp in configs)
            {
                _configStore[kvp.Key] = kvp.Value;
            }
        }

        public IReadOnlyDictionary<string, object?> GetAll()
        {
            return new Dictionary<string, object?>(_configStore);
        }
    }

    public class ConfigChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public string? OldValue { get; }

        public string? NewValue { get; }

        public ConfigChangedEventArgs(string key, string? oldValue, string? newValue)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}
