using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool
{
    /// <summary>
    /// 通用任务池（状态机 canonical 实现）。组合：
    /// <see cref="TaskStateMachine"/>（任务生命周期）+ <see cref="ITaskPoolPersistence{TItem}"/>（持久化镜像）
    /// + <see cref="TaskPauseHandle"/>（任务级暂停，TCS）+ <see cref="TaskPauseManager"/>（队列级全局暂停）+ SemaphoreSlim（并发闸门）。
    /// 写入（Enqueue/Payload 持久化）→ 激活标志（Active，与写入解耦）→ 唤醒消费循环；暂停为循环内非终止决策（Running ⇄ Paused）。
    /// 消费门仅采用「NotStarted 且 Active=true」的任务；休眠（Active=false）仅持久化不被消费，经 <see cref="SetActiveAsync"/> 翻转后唤醒。
    /// <para>触发架构（三层单一机制，**无时间窗节流**）：
    /// ① 事件驱动——写即唤醒（Enqueue/SetActive/回填/完成触发均写 <c>_signal</c>），非周期轮询；
    /// ② 信号合并——一次唤醒先排空全部待处理信号再 drain 一次，写风暴折叠为单次持久化查询；
    /// ③ 空闲兜底——<see cref="TaskPoolOptions.IdleReconcileIntervalMs"/> 超周期补跑（唯一节流点，防信号丢失睡死）。
    /// 任务启动不节流（写即激活即消费）：时间窗式全局限频与「写即激活」事件语义冲突，且其声称保护的持久化查询发生在门控之前，
    /// 且与①②③职责重叠（机制冗余），故已废除（见 options 说明）。</para>
    /// 与 Agent 无关（通用基础框架内自包含状态原语，不依赖 Modules.StateMachine / Share.StateManagement）。
    /// </summary>
    public sealed class TaskPool<TItem, TResult> : ITaskPool<TItem>
        where TItem : ITaskPoolItem
    {
        private readonly ITaskPoolPersistence<TItem> _persistence;
        private readonly TaskPoolHandler<TItem, TResult> _handler;
        private readonly int _maxConcurrency;
        private readonly int _batchSize;
        private readonly int _heartbeatIntervalMs;
        private readonly TimeSpan _drainTimeout;
        private readonly TimeSpan _idleReconcile;

        private readonly TaskStateStore _states = new();
        private readonly ConcurrentDictionary<string, TaskPauseHandle> _pauses = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _retries = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
        private readonly TaskPauseManager _pauseManager = new();
        private readonly SemaphoreSlim _gate;
        private readonly Channel<bool> _signal;

        private readonly object _lock = new();
        private CancellationTokenSource? _loopCts;
        private Task? _consumer;
        private Timer? _reconcileTimer;
        private volatile bool _started;
        private bool _disposed;

        /// <summary>任务状态变更（写即触发观测）。</summary>
        public event EventHandler<TaskPoolStateChangedEventArgs>? TaskStateChanged;

        /// <summary>消费循环单次 drain 失败事件（诊断）：记录后消费循环自愈续跑（不退出），宿主可订阅取证根因。</summary>
        public event EventHandler<Exception>? DrainFault;

        /// <summary>消费循环阶段观测（诊断）：每次 drain 的结束（耗时/待办数/启动键/故障原因）。
        /// 用于把「已入队却长时间未启动」判定为「drain 被阻塞（耗时长）」vs「拉取为空（pending=0）」——任务池不再是不透明黑盒。</summary>
        public event EventHandler<TaskPoolConsumptionEventArgs>? ConsumptionObserved;

        public TaskPool(
            ITaskPoolPersistence<TItem> persistence,
            TaskPoolHandler<TItem, TResult> handler,
            TaskPoolOptions? options = null)
        {
            _persistence = PortGuard.Required(persistence, nameof(persistence));
            _handler = PortGuard.Required(handler, nameof(handler));
            options ??= new TaskPoolOptions();
            _maxConcurrency = Math.Max(1, options.MaxConcurrency);
            _batchSize = Math.Max(1, options.BatchSize);
            _heartbeatIntervalMs = Math.Max(0, options.HeartbeatIntervalMs);
            _drainTimeout = TimeSpan.FromMilliseconds(Math.Max(0, options.DrainTimeoutMs));
            _idleReconcile = TimeSpan.FromMilliseconds(Math.Max(0, options.IdleReconcileIntervalMs));
            _gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
            _signal = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true });
        }

        // ---- 生命周期 ----

        public void Start(CancellationToken ct = default)
        {
            if (_started) return;
            _started = true;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // 空闲兜底：定时把 tick 写入**同一唤醒通道**（而非旁路 Task.WhenAny+Task.Delay）——
            // 保证「单一读者、始终恰有一个挂起读」，消除放弃挂起读/每次迭代分配定时器的语义歧义。
            if (_idleReconcile > TimeSpan.Zero)
                _reconcileTimer = new Timer(_ => _signal.Writer.TryWrite(true), null, _idleReconcile, _idleReconcile);
            _consumer = Task.Run(() => ConsumeLoopAsync(_loopCts.Token));
        }

        public async Task StopAsync()
        {
            if (!_started) return;
            _started = false;
            var cts = _loopCts;
            _loopCts = null;
            var timer = _reconcileTimer;
            _reconcileTimer = null;
            timer?.Dispose();
            _signal.Writer.TryComplete();
            if (cts is not null)
            {
                cts.Cancel();
                try { if (_consumer is not null) await _consumer.ConfigureAwait(false); } catch { }
                cts.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await StopAsync().ConfigureAwait(false);
            _gate.Dispose();
            _pauseManager.ResumeGlobalPause();
        }

        // ---- 入队 / 激活 ----

        /// <summary>翻转任务激活标志（激活与写入解耦）：读取持久化快照 → 改 <see cref="IActivatableTaskPoolItem.Active"/> → 写回 PayloadJson。
        /// 仅当条目实现 <see cref="IActivatableTaskPoolItem"/> 才支持（否则返回 false）。激活（置 true）时唤醒消费循环，使
        /// 休眠任务（NotStarted 且此前 Active=false）重新可出队；置 false（休眠）仅持久化、不唤醒。</summary>
        public async Task<bool> SetActiveAsync(string taskKey, bool active, CancellationToken ct = default)
        {
            var item = await _persistence.GetAsync(taskKey, ct).ConfigureAwait(false);
            if (item is not IActivatableTaskPoolItem activatable) return false;
            activatable.Active = active;
            await _persistence.UpdatePayloadAsync(taskKey, item, ct).ConfigureAwait(false);
            if (active) await _signal.Writer.WriteAsync(true, ct).ConfigureAwait(false);
            return true;
        }

        public async Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrWhiteSpace(item.TaskKey))
                throw new ArgumentException("TaskKey cannot be empty", nameof(item));

            var id = await _persistence.EnqueueAsync(item, ct).ConfigureAwait(false);
            // 可重跑语义：重新入队时，仅当内存态为终态/幽灵态（Completed/Faulted/Cancelled）才重置为 NotStarted；
            // 否则 GetOrCreate 返回旧终态会导致任务不再出队（清理后重跑同 taskKey 失效）。在途（Running/Paused）由上层守卫拦截，不在此重置。
            var state = _states.GetOrCreate(item.TaskKey);
            var cur = state.CurrentState;
            if (cur is TaskExecutionStatus.Completed or TaskExecutionStatus.Faulted or TaskExecutionStatus.Cancelled)
            {
                state.TryTransitionTo(TaskExecutionStatus.NotStarted, _ => true);
                _retries.TryRemove(item.TaskKey, out _);
            }
            await _signal.Writer.WriteAsync(true, ct).ConfigureAwait(false);
            // 状态迁移真值：from 取复位前真实内存态（消除恒 NotStarted→NotStarted 的日志失真——取消/完成后重跑可见 Cancelled/Completed→NotStarted）。
            Emit(item.TaskKey, cur, TaskExecutionStatus.NotStarted, "enqueued");
            return id;
        }

        // ---- 暂停 / 恢复 / 取消（双源收敛：Loop 主动 + External 强制）----

        public async Task PauseAsync(string taskKey, string? source = null, string? reason = null, CancellationToken ct = default)
        {
            var state = _states.GetOrCreate(taskKey);
            bool isInFlight;
            lock (_lock) isInFlight = _inFlight.Contains(taskKey);

            if (isInFlight && state.CurrentState == TaskExecutionStatus.Running)
            {
                if (state.TryPause())
                {
                    _pauses.GetOrAdd(taskKey, _ => new TaskPauseHandle()).Pause();
                    await _persistence.UpdateStatusAsync(taskKey, TaskExecutionStatus.Running, TaskExecutionStatus.Paused, ct).ConfigureAwait(false);
                    Emit(taskKey, TaskExecutionStatus.Running, TaskExecutionStatus.Paused, reason ?? "paused");
                }
            }
            else if (state.CurrentState == TaskExecutionStatus.NotStarted)
            {
                if (state.TryTransitionTo(TaskExecutionStatus.Paused, s => s == TaskExecutionStatus.NotStarted))
                {
                    await _persistence.UpdateStatusAsync(taskKey, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Paused, ct).ConfigureAwait(false);
                    Emit(taskKey, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Paused, reason ?? "paused");
                }
            }
        }

        public async Task ResumeAsync(string taskKey, CancellationToken ct = default)
        {
            var state = _states.GetOrCreate(taskKey);
            if (state.CurrentState != TaskExecutionStatus.Paused) return;

            if (_pauses.TryGetValue(taskKey, out var handle) && handle.IsPaused)
            {
                handle.Resume(); // 唤醒在途处理体
                if (state.TryResume())
                {
                    await _persistence.UpdateStatusAsync(taskKey, TaskExecutionStatus.Paused, TaskExecutionStatus.Running, ct).ConfigureAwait(false);
                    Emit(taskKey, TaskExecutionStatus.Paused, TaskExecutionStatus.Running, "resumed");
                }
            }
            else if (state.TryTransitionTo(TaskExecutionStatus.NotStarted, s => s == TaskExecutionStatus.Paused))
            {
                await _persistence.UpdateStatusAsync(taskKey, TaskExecutionStatus.Paused, TaskExecutionStatus.NotStarted, ct).ConfigureAwait(false);
                Emit(taskKey, TaskExecutionStatus.Paused, TaskExecutionStatus.NotStarted, "resumed");
                await _signal.Writer.WriteAsync(true, ct).ConfigureAwait(false); // 重新可出队
            }
        }

        public async Task CancelAsync(string taskKey, CancellationToken ct = default)
        {
            var state = _states.GetOrCreate(taskKey);
            var from = state.CurrentState;
            if (from is TaskExecutionStatus.Completed or TaskExecutionStatus.Faulted or TaskExecutionStatus.Cancelled)
                return;

            if (from == TaskExecutionStatus.Running && _pauses.TryGetValue(taskKey, out var h) && h.IsPaused)
                h.Resume(); // 释放挂起，交由上游取消

            if (state.TryCancel())
            {
                await _persistence.UpdateStatusAsync(taskKey, from, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
                Emit(taskKey, from, TaskExecutionStatus.Cancelled, "cancelled");
            }
        }

        // ---- 全局暂停 / 恢复 ----

        public async Task<int> PauseAllAsync(CancellationToken ct = default)
        {
            int count = 0;
            foreach (var id in _states.GetAllIds())
            {
                var state = _states.Get(id);
                if (state is null) continue;
                var from = state.CurrentState;
                if (from == TaskExecutionStatus.Running)
                {
                    _pauses.GetOrAdd(id, _ => new TaskPauseHandle()).Pause();
                    if (state.TryPause())
                    {
                        await _persistence.UpdateStatusAsync(id, TaskExecutionStatus.Running, TaskExecutionStatus.Paused, ct).ConfigureAwait(false);
                        Emit(id, TaskExecutionStatus.Running, TaskExecutionStatus.Paused, "paused-all");
                        count++;
                    }
                }
                else if (from == TaskExecutionStatus.NotStarted
                        && state.TryTransitionTo(TaskExecutionStatus.Paused, s => s == TaskExecutionStatus.NotStarted))
                {
                    await _persistence.UpdateStatusAsync(id, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Paused, ct).ConfigureAwait(false);
                    Emit(id, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Paused, "paused-all");
                    count++;
                }
            }
            return count;
        }

        public async Task<int> ResumeAllAsync(CancellationToken ct = default)
        {
            int count = 0;
            foreach (var id in _states.GetAllIds())
            {
                var state = _states.Get(id);
                if (state is null || state.CurrentState != TaskExecutionStatus.Paused) continue;

                if (_pauses.TryGetValue(id, out var handle) && handle.IsPaused)
                {
                    handle.Resume();
                    if (state.TryResume())
                    {
                        await _persistence.UpdateStatusAsync(id, TaskExecutionStatus.Paused, TaskExecutionStatus.Running, ct).ConfigureAwait(false);
                        Emit(id, TaskExecutionStatus.Paused, TaskExecutionStatus.Running, "resumed-all");
                        count++;
                    }
                }
                else if (state.TryTransitionTo(TaskExecutionStatus.NotStarted, s => s == TaskExecutionStatus.Paused))
                {
                    await _persistence.UpdateStatusAsync(id, TaskExecutionStatus.Paused, TaskExecutionStatus.NotStarted, ct).ConfigureAwait(false);
                    Emit(id, TaskExecutionStatus.Paused, TaskExecutionStatus.NotStarted, "resumed-all");
                    count++;
                }
            }
            if (count > 0)
                await _signal.Writer.WriteAsync(true, ct).ConfigureAwait(false);
            return count;
        }

        // ---- 查询 / 合作式挂起 ----

        public Task<TaskExecutionStatus> GetStatusAsync(string taskKey, CancellationToken ct = default)
        {
            var state = _states.Get(taskKey);
            return Task.FromResult(state?.CurrentState ?? TaskExecutionStatus.NotStarted);
        }

        public Task WaitIfPausedAsync(string taskKey, CancellationToken ct = default)
        {
            if (_pauses.TryGetValue(taskKey, out var handle) && handle.IsPaused)
                return handle.WaitIfPausedAsync(ct);
            return Task.CompletedTask;
        }

        // ---- 回填（重启续跑 / 手动驱动）----

        public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
        {
            // Running（进程内已死）→ NotStarted 重新可出队（at-least-once）；Paused 保持挂起。
            var running = await _persistence.ListAsync(TaskExecutionStatus.Running, int.MaxValue, ct).ConfigureAwait(false);
            foreach (var item in running)
            {
                var state = _states.GetOrCreate(item.TaskKey);
                state.TryTransitionTo(TaskExecutionStatus.NotStarted, s => s == TaskExecutionStatus.Running);
                await _persistence.UpdateStatusAsync(item.TaskKey, TaskExecutionStatus.Running, TaskExecutionStatus.NotStarted, ct).ConfigureAwait(false);
            }

            var paused = await _persistence.ListAsync(TaskExecutionStatus.Paused, int.MaxValue, ct).ConfigureAwait(false);
            foreach (var item in paused)
            {
                var state = _states.GetOrCreate(item.TaskKey);
                if (state.CurrentState == TaskExecutionStatus.NotStarted)
                    state.TryTransitionTo(TaskExecutionStatus.Paused, s => s == TaskExecutionStatus.NotStarted);
                _pauses.GetOrAdd(item.TaskKey, _ => new TaskPauseHandle()).Pause();
            }

            // 事件驱动重启：一个信号即驱动整条消费链（每个完成触发取下一个），无需一次性 drain 空。
            _signal.Writer.TryWrite(true);
            return (await _persistence.ListAsync(TaskExecutionStatus.NotStarted, int.MaxValue, ct).ConfigureAwait(false)).Count;
        }

        // ---- 内部 ----

        private async Task ConsumeLoopAsync(CancellationToken ct)
        {
            try
            {
                while (await WaitSignalAsync(ct).ConfigureAwait(false))
                {
                    while (_signal.Reader.TryRead(out _)) { }
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var (pending, started, skipped, skippedRows) = await DrainOnceAsync(ct).ConfigureAwait(false);
                        sw.Stop();
                        ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                            TaskPoolConsumptionPhase.DrainEnd, sw.ElapsedMilliseconds, pending, started, skipped: skipped,
                            skippedRows: skippedRows));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // 单次 drain 失败（异常/超时）只记一次并续跑（自愈），
                        // 避免任何单点异常/卡死杀死整条消费循环 → 后续新入队任务永远不被消费（hang）。
                        sw.Stop();
                        ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                            TaskPoolConsumptionPhase.DrainFault, sw.ElapsedMilliseconds, 0, null,
                            $"{ex.GetType().Name}: {ex.Message}"));
                        DrainFault?.Invoke(this, ex);
                        TriggerNext();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        /// <summary>等待唤醒：单一唤醒通道（事件信号 + 空闲兜底 tick 同源写入），返回 false = 通道已完成（应退出循环）。
        /// 空闲兜底不再经 <c>Task.WhenAny(Task.Delay)</c>（会放弃挂起读、每次迭代分配定时器），而由 <see cref="Start"/> 的定时器写入本通道。</summary>
        private async Task<bool> WaitSignalAsync(CancellationToken ct)
            => await _signal.Reader.WaitToReadAsync(ct).ConfigureAwait(false);

        private async Task<(int Pending, string? Started, int Skipped,
            List<(string TaskKey, TaskExecutionStatus MemoryState)> SkippedRows)> DrainOnceAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 队列级全局暂停（TCS 反应式，零轮询）：暂停"派发新任务"（在途自然跑完）。
            await DrainAsync(t => _pauseManager.WaitForResumeAsync(t), ct).ConfigureAwait(false);
            var pending = await DrainAsync(t => _persistence.ListAsync(TaskExecutionStatus.NotStarted, _batchSize, t), ct).ConfigureAwait(false);
            // 激活门：激活标志（Active）与写入解耦 —— 仅采用「NotStarted 且 Active=true」的任务；Active=false（休眠）仅持久化不被消费。
            // 未实现 IActivatableTaskPoolItem 的条目恒视为 Active=true（无门控，向后兼容）。
            var usable = pending.Where(IsActive).ToList();
            if (usable.Count == 0) return (0, null, 0, new());

            // 候选遍历（而非只看排序第一件）+ 陈旧行对齐：
            // 「持久化行可消费（NotStarted ∧ Active）但内存态已非 NotStarted」= 状态分叉（毒化/陈旧行）。
            // 旧实现对此走 `TriggerNext(); return;`——立即再唤醒→再选中同一行→**忙等自旋**，
            // 且每次只试一件 ⇒ 毒化行**队首阻塞**真实任务（表现为随机长时间滞留 NotStarted）。
            // 现改为：跳过该行并把其持久化状态**对齐到内存真态**（一次性消除毒化源），继续尝试其余候选。
            TItem? item = default;
            var skipped = 0;
            // 陈旧/毒化行明细（键 + 内存真态）：随 drain 上报，供宿主逐键留痕「分叉已对齐」。
            var skippedRows = new List<(string TaskKey, TaskExecutionStatus MemoryState)>();
            foreach (var candidate in usable.OrderByDescending(p => p.Priority))
            {
                var candidateState = _states.GetOrCreate(candidate.TaskKey);
                if (candidateState.CurrentState != TaskExecutionStatus.NotStarted)
                {
                    skipped++;
                    skippedRows.Add((candidate.TaskKey, candidateState.CurrentState));
                    await ReconcileStaleRowAsync(candidate.TaskKey, candidateState.CurrentState, ct).ConfigureAwait(false);
                    continue;
                }
                if (!candidateState.TryStart())
                {
                    skipped++;
                    continue;
                }
                item = candidate;
                break;
            }
            if (item is null) return (usable.Count, null, skipped, skippedRows); // 全部候选不可启动：不再自旋（交由下一信号/兜底重试）

            var key = item.TaskKey;

            await DrainAsync(t => _persistence.UpdateStatusAsync(key, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running, t), ct).ConfigureAwait(false);
            lock (_lock) _inFlight.Add(key);
            Emit(key, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running, "started");

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            // 在途心跳：长期执行（如长 LLM 调用）周期刷新 UpdatedAt，使 stuck Running 可被观测；随处理体结束/取消同源释放。
            using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeat = HeartbeatLoopAsync(key, hbCts.Token);
            try
            {
                await _handler(item, key, ct).ConfigureAwait(false);
                var st = _states.Get(key);
                if (st is not null && st.CurrentState == TaskExecutionStatus.Running && st.TryComplete())
                {
                    await _persistence.UpdateStatusAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Completed, ct).ConfigureAwait(false);
                    Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Completed, "completed");
                }
            }
            catch (OperationCanceledException)
            {
                var st = _states.Get(key);
                if (st is not null && st.TryCancel())
                {
                    await _persistence.UpdateStatusAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
                    Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled, "cancelled");
                }
            }
            catch (Exception)
            {
                var st = _states.Get(key);
                int retry = _retries.AddOrUpdate(key, 1, (_, v) => v + 1);
                if (st is not null && retry <= item.MaxRetries)
                {
                    st.TryTransitionTo(TaskExecutionStatus.NotStarted, s => s == TaskExecutionStatus.Running);
                    await _persistence.UpdateStatusAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.NotStarted, ct).ConfigureAwait(false);
                    Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.NotStarted, "retry");
                }
                else if (st is not null && st.TryFault())
                {
                    await _persistence.UpdateStatusAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, ct).ConfigureAwait(false);
                    Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, "faulted");
                }
            }
            finally
            {
                hbCts.Cancel();
                // 心跳纯观测（周期刷新 UpdatedAt）：**不得**在消费关键路径上 await——其持久化写入若阻塞，
                // 会顺带冻结整条消费链（已就绪的下一个任务也只能排队等待）。此处仅取消并观测异常，不阻塞后续 TriggerNext。
                _ = heartbeat.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                _gate.Release();
                lock (_lock) _inFlight.Remove(key);
            }

            // 状态变更（完成/失败/取消/重试）触发取下一个：事件驱动链（入队→完成→取下一个），无内层自转、无定时。
            TriggerNext();
            return (usable.Count, key, skipped, skippedRows);
        }

        /// <summary>陈旧/毒化行对齐：持久化行仍处可消费态（NotStarted ∧ Active）但内存态已非 NotStarted 时，
        /// 把持久化状态对齐到内存真态（内存态为权威：由同一状态机写入），使该行不再被 ListAsync 命中 → 一次性消除毒化源、终止自旋。
        /// 对齐失败不抛（不翻转消费循环）：本轮不再自旋重试，下一轮信号/空闲兜底再试。</summary>
        private async Task ReconcileStaleRowAsync(string taskKey, TaskExecutionStatus actual, CancellationToken ct)
        {
            try
            {
                await _persistence.UpdateStatusAsync(taskKey, TaskExecutionStatus.NotStarted, actual, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 对齐属可恢复的收敛动作：失败留给下一轮，绝不在本轮自旋 */ }
        }

        private void TriggerNext() => _signal.Writer.TryWrite(true);

        /// <summary>对 drain 读/写侧 await 施加有界等待（配置 <see cref="TaskPoolOptions.DrainTimeoutMs"/> &gt; 0 时生效）：
        /// 卡死（异步互斥/DB 读锁等）到点抛 <see cref="TimeoutException"/> 而非无限等待，由消费循环自愈续跑。
        /// 未配置（=0）时完全透传原调用（向后兼容）。</summary>
        private async Task DrainAsync(Func<CancellationToken, Task> op, CancellationToken ct)
        {
            if (_drainTimeout <= TimeSpan.Zero) { await op(ct).ConfigureAwait(false); return; }
            using var tct = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tct.CancelAfter(_drainTimeout);
            try { await op(tct.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"TaskPool drain 操作等待超时（{_drainTimeout.TotalMilliseconds}ms）");
            }
        }

        /// <summary>同上，带返回值的变体。</summary>
        private async Task<T> DrainAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
        {
            if (_drainTimeout <= TimeSpan.Zero) return await op(ct).ConfigureAwait(false);
            using var tct = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tct.CancelAfter(_drainTimeout);
            try { return await op(tct.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"TaskPool drain 操作等待超时（{_drainTimeout.TotalMilliseconds}ms）");
            }
        }

        /// <summary>激活判定：实现 <see cref="IActivatableTaskPoolItem"/> 的条目以 Active 标志为准；未实现（无激活语义）恒真（向后兼容）。</summary>
        private static bool IsActive(TItem item)
            => item is not IActivatableTaskPoolItem activatable || activatable.Active;

        /// <summary>在途任务心跳循环：周期以 Running→Running 条件更新刷新持久化 UpdatedAt（WHERE Status=Running，暂停/完成时自然失效），
        /// 使长期执行（长 LLM 调用）的 stuck Running 可经更新时间陈旧度被外部区分「工作中 vs 挂起/锁死」。心跳失败不阻断执行（仅观测性）。</summary>
        private async Task HeartbeatLoopAsync(string taskKey, CancellationToken ct)
        {
            if (_heartbeatIntervalMs <= 0) return;
            var interval = TimeSpan.FromMilliseconds(_heartbeatIntervalMs);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    try
                    {
                        await _persistence.UpdateStatusAsync(
                            taskKey, TaskExecutionStatus.Running, TaskExecutionStatus.Running, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 心跳写失败（如瞬时锁竞争）不阻断处理体，下一周期自然重试。
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void Emit(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, string? reason)
            => TaskStateChanged?.Invoke(this, new TaskPoolStateChangedEventArgs(taskKey, from, to, reason));
    }
}
