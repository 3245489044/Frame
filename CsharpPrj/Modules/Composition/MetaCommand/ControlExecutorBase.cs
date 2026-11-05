using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.StateMachine;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace MetaCommand
{
    public abstract class ControlExecutorBase : BaseCommandExecutor
    {
        protected IControlService ControlService { get; }

        protected ControlExecutorBase(string executorId, string name, string version, IControlService controlService)
            : base(executorId, name, version)
        {
            ControlService = controlService ?? throw new ArgumentNullException(nameof(controlService));
        }


        protected void InvokeOrBeginOnControl(object control, Action action)
        {
            if (control is System.Windows.Forms.Control c && c.InvokeRequired)
                c.BeginInvoke(action);
            else
                action();
        }

        protected T InvokeOnControl<T>(object control, Func<T> func)
        {
            if (control is System.Windows.Forms.Control c && c.InvokeRequired)
                return (T)c.Invoke(func);
            else
                return func();
        }



        public override async Task<OperationResult<object>> DoAsync(
            string action, string? target, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(target))
                return OperationResult<object>.FailureResult("Target is required", ErrorCode.InvalidParameter);

            var control = ControlService.FindByName(target);
            if (control == null)
                return OperationResult<object>.FailureResult($"控件未找到: {target}", ErrorCode.TargetNotFound);

            return action?.ToLower() switch
            {
                "set"        => await DoSetAsync(control, parameters, ct),
                "click"      => await DoClickAsync(control, ct),
                "doubleclick" => await DoDoubleClickAsync(control, ct),
                "enable"     => await DoEnableAsync(control, true, ct),
                "disable"    => await DoEnableAsync(control, false, ct),
                "show"       => await DoVisibleAsync(control, true, ct),
                "hide"       => await DoVisibleAsync(control, false, ct),
                _ => OperationResult<object>.FailureResult($"不支持的操作: {action}", ErrorCode.ActionNotFound)
            };
        }

        protected virtual Task<OperationResult<object>> DoSetAsync(object control, Dictionary<string, object>? parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var value = parameters?.GetValueOrDefault("value")?.ToString();
            InvokeOrBeginOnControl(control, () => ControlService.SetValue(control, value));
            return Task.FromResult(OperationResult<object>.SuccessResult(value, $"已设置"));
        }

        protected virtual Task<OperationResult<object>> DoClickAsync(object control, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            InvokeOrBeginOnControl(control, () =>
                control.GetType().GetMethod("PerformClick")?.Invoke(control, null));
            return Task.FromResult(OperationResult<object>.SuccessResult(null, "已点击"));
        }

        protected virtual Task<OperationResult<object>> DoDoubleClickAsync(object control, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            InvokeOrBeginOnControl(control, () =>
            {
                // Button 走 PerformClick；其余控件（如 ListView）经 OnDoubleClick 反射触发 DoubleClick 事件，
                // 与 GUI 人手双击走同一事件处理器（模拟真实双击）
                if (control.GetType().GetMethod("PerformClick") != null)
                {
                    control.GetType().GetMethod("PerformClick")!.Invoke(control, null);
                    return;
                }

                var onDoubleClick = control.GetType().GetMethod("OnDoubleClick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                onDoubleClick?.Invoke(control, new object[] { EventArgs.Empty });
            });
            return Task.FromResult(OperationResult<object>.SuccessResult(null, "已双击"));
        }

        protected virtual Task<OperationResult<object>> DoEnableAsync(object control, bool enabled, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            InvokeOrBeginOnControl(control, () =>
                control.GetType().GetProperty("Enabled")?.SetValue(control, enabled));
            return Task.FromResult(OperationResult<object>.SuccessResult(enabled, enabled ? "已启用" : "已禁用"));
        }

        protected virtual Task<OperationResult<object>> DoVisibleAsync(object control, bool visible, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            InvokeOrBeginOnControl(control, () =>
                control.GetType().GetProperty("Visible")?.SetValue(control, visible));
            return Task.FromResult(OperationResult<object>.SuccessResult(visible, visible ? "已显示" : "已隐藏"));
        }



        public override async Task<OperationResult<object>> GetAsync(
            string entity, Dictionary<string, object>? queryParams, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(entity))
                return OperationResult<object>.FailureResult("Entity is required", ErrorCode.InvalidParameter);

            var control = ControlService.FindByName(entity);
            if (control == null)
                return OperationResult<object>.FailureResult($"控件未找到: {entity}", ErrorCode.TargetNotFound);

            var value = InvokeOnControl(control, () => ControlService.GetValue(control));
            return OperationResult<object>.SuccessResult(value, $"已读取 {entity}");

        }


        public override async Task<OperationResult<WorldModel>> LookAsync(
            string? scope, Dictionary<string, object>? options, CancellationToken ct = default)
        {
            var model = new WorldModel
            {
                AdapterId = ExecutorId,
                Scope = scope ?? "all",
                Timestamp = DateTime.UtcNow
            };
            await BuildWorldModelAsync(model, scope, ct);
            return OperationResult<WorldModel>.SuccessResult(model, "已查看控件树");
        }

        protected abstract Task BuildWorldModelAsync(WorldModel model, string? scope, CancellationToken ct);


        public override async Task<OperationResult<bool>> WaitAsync(
            string condition, int timeoutMs, CancellationToken ct = default)
        {
            var resolveResult = ResolveControlCondition(condition);
            if (!resolveResult.IsValid)
                return OperationResult<bool>.FailureResult(resolveResult.ErrorMessage!, ErrorCode.InvalidParameter);

            var (control, _, expectedValue) = resolveResult;
            var eventName = InferEventName(resolveResult.PropertyName);

            var awaiter = new AsyncEventStateMachine<EventArgs, bool>();

            EventHandler handler = (s, e) => awaiter.OnEvent(EventArgs.Empty);
            ControlService.BindEvent(control, eventName, handler);
            try
            {
                var result = await awaiter.WaitAsync(
                    _ =>
                    {
                        var currentValue = InvokeOnControl(control, () => ControlService.GetValue(control));
                        return currentValue?.ToString() == expectedValue;
                    },

                    timeoutMs: timeoutMs,
                    cancellationToken: ct);
                return OperationResult<bool>.SuccessResult(true, "条件已满足");
            }
            catch (TimeoutException)
            {
                return OperationResult<bool>.FailureResult("等待超时", ErrorCode.Timeout);
            }
            catch (OperationCanceledException)
            {
                return OperationResult<bool>.FailureResult("等待操作被取消", ErrorCode.OperationCanceled);
            }
            finally
            {
                ControlService.UnbindEvent(control, eventName);
                awaiter.Reset();
            }
        }


        public override async Task<OperationResult<bool>> AssertAsync(
            string predicate, Dictionary<string, object>? parameters, CancellationToken ct = default)
        {
            var resolveResult = ResolveControlCondition(predicate);
            if (!resolveResult.IsValid)
                return OperationResult<bool>.FailureResult(resolveResult.ErrorMessage!, ErrorCode.InvalidParameter);

            var (control, _, expectedValue) = resolveResult;
            var actualValue = InvokeOnControl(control, () => ControlService.GetValue(control));

            var isMatch = actualValue?.ToString() == expectedValue;

            return OperationResult<bool>.SuccessResult(isMatch,
                isMatch ? "断言通过" : $"断言失败: 期望 '{expectedValue}', 实际 '{actualValue}'");
        }


        public override async Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(
            string? scope, CancellationToken ct = default)
        {
            var model = new WorldModel();
            await BuildWorldModelAsync(model, scope, ct);
            return OperationResult<List<EntityDescriptor>>.SuccessResult(model.Entities, $"已发现 {model.Entities.Count} 个控件");
        }


        protected ControlConditionResult ResolveControlCondition(string condition)
        {
            var (controlName, propertyName, expectedValue) = ParseCondition(condition);
            if (string.IsNullOrEmpty(controlName))
                return ControlConditionResult.Invalid("无效的条件表达式");

            var control = ControlService.FindByName(controlName);
            if (control == null)
                return ControlConditionResult.Invalid($"控件未找到: {controlName}");

            return ControlConditionResult.Valid(control, propertyName, expectedValue);
        }

        private static (string? ControlName, string? PropertyName, string? ExpectedValue) ParseCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return (null, null, null);

            var parts = condition.Split(new[] { "==" }, 2, StringSplitOptions.None);
            if (parts.Length != 2) return (null, null, null);

            var left = parts[0].Trim();
            var expectedValue = parts[1].Trim().Trim('\'', '"');

            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(expectedValue))
                return (null, null, null);

            var dotIndex = left.IndexOf('.');
            return dotIndex > 0
                ? (left[..dotIndex], left[(dotIndex + 1)..], expectedValue)
                : (left, "Value", expectedValue);
        }

        private static string InferEventName(string? propertyName) => propertyName?.ToLower() switch
        {
            "text"          => "TextChanged",
            "checked"       => "CheckedChanged",
            "value"         => "ValueChanged",
            "selectedindex" => "SelectedIndexChanged",
            "selecteditem"  => "SelectedIndexChanged",
            _               => "TextChanged"
        };

        protected class ControlConditionResult
        {
            public bool IsValid { get; }
            public string? ErrorMessage { get; }
            public object? Control { get; }
            public string? PropertyName { get; }
            public string? ExpectedValue { get; }

            private ControlConditionResult(bool isValid, string? errorMessage, object? control, string? propertyName, string? expectedValue)
            {
                IsValid = isValid;
                ErrorMessage = errorMessage;
                Control = control;
                PropertyName = propertyName;
                ExpectedValue = expectedValue;
            }

            public static ControlConditionResult Valid(object control, string? propertyName, string? expectedValue)
                => new(true, null, control, propertyName, expectedValue);

            public static ControlConditionResult Invalid(string errorMessage)
                => new(false, errorMessage, null, null, null);

            public void Deconstruct(out object? control, out string? propertyName, out string? expectedValue)
            {
                control = Control;
                propertyName = PropertyName;
                expectedValue = ExpectedValue;
            }
        }
    }
}
