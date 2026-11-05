using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading
{
    /// <summary>
    /// 可取消/可预算的执行边界载体（Modules.Threading 上收，原 Operation.HierarchyMethod.Center.ExecutionScope 的通用归属）。
    /// 以 CTS 根 + 层级 <see cref="Derive"/> 子边界把任一 await 纳入有界执行：自身预算窗口耗尽抛
    /// <see cref="BoundedTimeoutException"/> 强类型结果（非 OperationCanceled），杜绝『裸 await 不受控』。
    /// <para>
    /// 预算语义对齐原实现：外层取消优先（外层取消一律按取消上抛、不误报超时），仅自身预算耗尽才抛强类型；
    /// 未给预算（null）时退化为直跑（仅响应取消），默认零误杀。预算**不跨级自动继承**——给某级限界须在该级
    /// <see cref="Derive"/> 显式赋 budget，或调用 Run*(timeout:) 显式给。
    /// </para>
    /// </summary>
    public sealed class BoundedExecution : IDisposable
    {
        private readonly string _name;
        private readonly CancellationTokenSource _cts;
        private readonly TimeSpan? _defaultBudget;
        private readonly Action<BoundedTimeoutException>? _onStall;

        private BoundedExecution(
            string name,
            CancellationToken parentToken,
            TimeSpan? defaultBudget,
            Action<BoundedTimeoutException>? onStall)
        {
            _name = name;
            _cts = parentToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
                : new CancellationTokenSource();
            _defaultBudget = defaultBudget;
            _onStall = onStall;
        }

        /// <summary>创建根边界。budget = 根自身默认预算（整轮有界）；external 把既有多线程取消并入根；onStall 为超时诊断钩子（非阻塞）。</summary>
        public static BoundedExecution CreateRoot(
            string name,
            TimeSpan? budget = null,
            CancellationToken external = default,
            Action<BoundedTimeoutException>? onStall = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            return new BoundedExecution(name, external, budget, onStall);
        }

        public string Name => _name;

        public CancellationToken Token => _cts.Token;

        public bool IsCancellationRequested => _cts.IsCancellationRequested;

        /// <summary>派生层级子边界：独立 linked-CTS（可单独取消、级联父取消），可覆盖默认预算。</summary>
        public BoundedExecution Derive(string name, TimeSpan? budget = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            return new BoundedExecution(name, _cts.Token, budget, _onStall);
        }

        public void Cancel() => _cts.Cancel();

        public Task<T> RunStepAsync<T>(string subject, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
            => RunBoundedCoreAsync(subject, body, timeout, BoundedTimeoutKind.Step);

        public Task<T> RunRoundAsync<T>(string subject, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
            => RunBoundedCoreAsync(subject, body, timeout, BoundedTimeoutKind.Round);

        public Task<T> RunStoreAsync<T>(string subject, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
            => RunBoundedCoreAsync(subject, body, timeout, BoundedTimeoutKind.Store);

        public Task RunStepAsync(string subject, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
            => RunStepAsync<bool>(subject, Wrap(body), timeout);

        public Task RunRoundAsync(string subject, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
            => RunRoundAsync<bool>(subject, Wrap(body), timeout);

        public Task RunStoreAsync(string subject, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
            => RunStoreAsync<bool>(subject, Wrap(body), timeout);

        private static Func<CancellationToken, Task<bool>> Wrap(Func<CancellationToken, Task> body)
            => async t => { await body(t).ConfigureAwait(false); return true; };

        private async Task<T> RunBoundedCoreAsync<T>(
            string subject,
            Func<CancellationToken, Task<T>> body,
            TimeSpan? timeout,
            BoundedTimeoutKind kind)
        {
            ArgumentNullException.ThrowIfNull(body);
            var effective = timeout ?? _defaultBudget;
            if (effective is null || effective <= TimeSpan.Zero)
            {
                // 无预算 → 直跑（仅响应取消），未接线时不引入额外超时、零误杀。
                return await body(_cts.Token).ConfigureAwait(false);
            }

            using var run = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            run.CancelAfter(effective.Value);
            try
            {
                var task = body(run.Token);
                var winner = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, run.Token)).ConfigureAwait(false);
                if (!ReferenceEquals(winner, task))
                {
                    // 自身预算耗尽（非外层取消）：外层取消时 run 因 linked 已取消而让 Delay 完成 → 先判外层再判超时。
                    if (_cts.IsCancellationRequested) _cts.Token.ThrowIfCancellationRequested();
                    var ex = new BoundedTimeoutException(kind, subject, effective.Value);
                    _onStall?.Invoke(ex);
                    throw ex;
                }
                return await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
            {
                // 在途体观察到本窗口取消（合作式）→ 转强类型超时，避免上层按整轮取消误判。
                var ex = new BoundedTimeoutException(kind, subject, effective.Value);
                _onStall?.Invoke(ex);
                throw ex;
            }
        }

        public void Dispose()
        {
            _cts.Cancel(); // 释放即级联取消本子树，阻断在途未完成体继续占用
            _cts.Dispose();
        }
    }
}
