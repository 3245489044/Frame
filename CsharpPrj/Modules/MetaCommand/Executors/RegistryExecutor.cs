
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;
using Microsoft.Win32;

namespace MetaCommand
{
    public class RegistryExecutor : BaseCommandExecutor
    {
        public RegistryExecutor()
            : base("registry", "Registry Executor", "1.0") { }


        public override Task<OperationResult<object>> DoAsync(
            string action, string? target, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(target))
                return Task.FromResult(OperationResult<object>.FailureResult("Target is required", ErrorCode.InvalidParameter));

            try
            {
                var value = parameters?.GetValueOrDefault("value")?.ToString() ?? string.Empty;
                var (hive, keyPath, valueName) = ParseRegistryPath(target);
                using var key = OpenRegistryKey(hive, keyPath, writable: true);
                if (key == null)
                    return Task.FromResult(OperationResult<object>.FailureResult(
                        $"无法打开注册表键: {hive}\\{keyPath}", ErrorCode.TargetNotFound));

                key.SetValue(valueName, value);
                return Task.FromResult(OperationResult<object>.SuccessResult(value,
                    $"已设置注册表值 {target} = {value}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<object>.FailureResult(
                    $"设置注册表值失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override Task<OperationResult<object>> GetAsync(
            string entity, Dictionary<string, object>? queryParams, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entity))
                return Task.FromResult(OperationResult<object>.FailureResult("Entity is required", ErrorCode.InvalidParameter));

            try
            {
                var (hive, keyPath, valueName) = ParseRegistryPath(entity);
                using var key = OpenRegistryKey(hive, keyPath, writable: false);
                if (key == null)
                    return Task.FromResult(OperationResult<object>.FailureResult(
                        $"无法打开注册表键: {hive}\\{keyPath}", ErrorCode.TargetNotFound));

                var value = key.GetValue(valueName);
                var result = value?.ToString() ?? "(null)";
                return Task.FromResult(OperationResult<object>.SuccessResult(result,
                    $"已读取 {entity}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<object>.FailureResult(
                    $"读取注册表值失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override Task<OperationResult<WorldModel>> LookAsync(
            string? scope, Dictionary<string, object>? options, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(scope))
                return Task.FromResult(OperationResult<WorldModel>.FailureResult("Scope is required", ErrorCode.InvalidParameter));

            try
            {
                var (hive, keyPath, _) = ParseRegistryPath(scope);
                using var key = OpenRegistryKey(hive, keyPath, writable: false);
                if (key == null)
                    return Task.FromResult(OperationResult<WorldModel>.FailureResult(
                        $"无法打开注册表键: {hive}\\{keyPath}", ErrorCode.TargetNotFound));

                var subKeyNames = key.GetSubKeyNames();
                var valueNames = key.GetValueNames();

                var model = new WorldModel
                {
                    AdapterId = ExecutorId,
                    Scope = scope,
                    Timestamp = DateTime.UtcNow
                };

                foreach (var subKey in subKeyNames)
                {
                    model.Entities.Add(new EntityDescriptor
                    {
                        Name = subKey,
                        Type = "RegistryKey"
                    });
                }

                foreach (var name in valueNames)
                {
                    var val = key.GetValue(name);
                    model.Entities.Add(new EntityDescriptor
                    {
                        Name = name,
                        Type = "RegistryValue",
                        Properties = new Dictionary<string, object>
                        {
                            ["Value"] = val?.ToString() ?? "(null)"
                        }
                    });
                }

                return Task.FromResult(OperationResult<WorldModel>.SuccessResult(model,
                    $"已列出 {scope}（{subKeyNames.Length} 个子键，{valueNames.Length} 个值）"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult<WorldModel>.FailureResult(
                    $"列出注册表键失败: {ex.Message}", ErrorCode.ExecutionFailed));
            }
        }


        public override Task<OperationResult<bool>> WaitAsync(
            string condition, int timeoutMs, CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<bool>.FailureResult(
                "Registry executor does not support Wait", ErrorCode.ActionNotFound));
        }


        public override Task<OperationResult<bool>> AssertAsync(
            string predicate, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(predicate))
                return Task.FromResult(OperationResult<bool>.FailureResult("Predicate is required", ErrorCode.InvalidParameter));

            try
            {
                var expectedValue = parameters?.GetValueOrDefault("expected")?.ToString();
                var (hive, keyPath, valueName) = ParseRegistryPath(predicate);
                using var key = OpenRegistryKey(hive, keyPath, writable: false);
                if (key == null)
                    return Task.FromResult(OperationResult<bool>.FailureResult(
                        $"无法打开注册表键: {hive}\\{keyPath}", ErrorCode.TargetNotFound));

                var actualValue = key.GetValue(valueName)?.ToString();
                var isMatch = string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);

                return Task.FromResult(OperationResult<bool>.SuccessResult(isMatch,
                    isMatch ? $"断言通过: {predicate} = {actualValue}"
                            : $"断言失败: 期望 '{expectedValue}', 实际 '{actualValue}'"));
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
                $"已发现 {(model.Data?.Entities?.Count ?? 0)} 个条目");
        }


        private static (RegistryHive hive, string keyPath, string valueName) ParseRegistryPath(string target)
        {
            target = target.Trim();

            string valueName = "";
            var colonIndex = target.LastIndexOf(':');
            string keyPart;
            if (colonIndex > 0 && colonIndex > target.IndexOf('\\'))
            {
                valueName = target[(colonIndex + 1)..].Trim();
                keyPart = target[..colonIndex].Trim();
            }
            else
            {
                keyPart = target;
            }

            var (hive, keyPath) = ParseHive(keyPart);
            return (hive, keyPath, string.IsNullOrEmpty(valueName) ? "" : valueName);
        }

        private static (RegistryHive hive, string keyPath) ParseHive(string path)
        {
            if (path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
                return (RegistryHive.LocalMachine, path[(path.IndexOf('\\') + 1)..]);
            if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                return (RegistryHive.CurrentUser, path[(path.IndexOf('\\') + 1)..]);
            if (path.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_CLASSES_ROOT\\", StringComparison.OrdinalIgnoreCase))
                return (RegistryHive.ClassesRoot, path[(path.IndexOf('\\') + 1)..]);
            if (path.StartsWith("HKU\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_USERS\\", StringComparison.OrdinalIgnoreCase))
                return (RegistryHive.Users, path[(path.IndexOf('\\') + 1)..]);
            if (path.StartsWith("HKCC\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_CURRENT_CONFIG\\", StringComparison.OrdinalIgnoreCase))
                return (RegistryHive.CurrentConfig, path[(path.IndexOf('\\') + 1)..]);

            return (RegistryHive.CurrentUser, path);
        }

        private static RegistryKey? OpenRegistryKey(RegistryHive hive, string keyPath, bool writable)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            return baseKey.OpenSubKey(keyPath, writable);
        }
    }
}
