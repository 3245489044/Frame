#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点输出单写协调器 —— R3 单一确定性写门。
    /// 语义：
    ///   ① <b>单写者 fail-fast</b>：同一 target 一旦有写者在写，任一平行写立即抛 <see cref="InvalidOperationException"/>，
    ///      不排队、不阻塞等待（防两个实现/能力同写一 target 造成竞争）。
    ///   ② <b>串行 + 有界等待</b>：每次写入经 target 级信号量串行化，等待受 <paramref name="boundedWait"/> 上界约束，
    ///      超时抛 <see cref="TimeoutException"/>。
    ///   ③ 目标以「工作流/workflowId」为主要判定键；同步路径与未来 Outbox 后台共用同一实例即保持确定性。
    /// 线程安全：active/locks 的增删经 <see cref="_gate"/> 串行；body 在信号量内执行。
    /// </summary>
    public sealed class NodePersistCoordinator
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _active = new(StringComparer.Ordinal);
        private readonly TimeSpan _defaultTimeout;

        public NodePersistCoordinator(TimeSpan? boundedWait = null)
        {
            _defaultTimeout = boundedWait ?? TimeSpan.FromSeconds(30);
        }

        public Task RunWriteAsync(string target, Func<CancellationToken, Task> body, CancellationToken ct = default)
            => RunWriteAsync(target, body, _defaultTimeout, ct);

        public async Task RunWriteAsync(
            string target,
            Func<CancellationToken, Task> body,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("写目标不能为空", nameof(target));
            ArgumentNullException.ThrowIfNull(body);

            SemaphoreSlim sem;
            lock (_gate)
            {
                if (_active.Contains(target))
                    throw new InvalidOperationException(
                        $"NodePersistCoordinator: 目标 '{target}' 已有写者在写，单写被并行破坏（fail-fast）。");

                _active.Add(target);
                if (!_locks.TryGetValue(target, out sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _locks[target] = sem;
                }
            }

            try
            {
                if (!await sem.WaitAsync(timeout, ct).ConfigureAwait(false))
                    throw new TimeoutException(
                        $"NodePersistCoordinator: 写目标 '{target}' 在等待上界（{timeout}）内未获得写门。");

                try { await body(ct).ConfigureAwait(false); }
                finally { sem.Release(); }
            }
            finally
            {
                lock (_gate) _active.Remove(target);
            }
        }
    }
}