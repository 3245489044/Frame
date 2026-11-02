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
    public sealed class TaskPool<TItem, TResult> : ITaskPool<TItem>, ITaskGroupPool<TItem>
        where TItem : ITaskPoolItem
    {
        private readonly ITaskPoolPersistence<TItem> _persistence;
        private readonly TaskPoolHandler<TItem, TResult> _handler;
        private readonly ITaskPoolItemInitializer<TItem>? _initializer;
        private readonly ITaskOutcomePolicy<TResult>? _outcomePolicy;
        private readonly ITaskGroupDependencyProbe? _dependencyProbe;
        private readonly int _maxConcurrency;
        private readonly int _batchSize;
        private readonly int _heartbeatIntervalMs;
        private readonly TimeSpan _drainTimeout;
        private readonly TimeSpan _idleReconcile;
        private readonly int _capacityWaitMs;
        private readonly int _groupMaxMembers;
        private readonly TaskGroupCompletion _groupDefaultCompletion;
        private readonly int _groupDefaultQuorum;
        private readonly bool _allowUnverifiedGroups;

        private readonly TaskStateStore _states = new();
        private readonly ConcurrentDictionary<string, TaskPauseHandle> _pauses = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskPoolItemKind> _kinds = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _activeFlags = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
        private readonly TaskPauseManager _pauseManager = new();
        private readonly SemaphoreSlim _gate;
        private readonly Channel<bool> _signal;
        private readonly ConcurrentDictionary<string, GroupState> _groups = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _taskGroup = new(StringComparer.Ordinal);
        private int _slotSeq;

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
            TaskPoolOptions? options = null,
            ITaskPoolItemInitializer<TItem>? initializer = null,
            ITaskOutcomePolicy<TResult>? outcomePolicy = null,
            ITaskGroupDependencyProbe? dependencyProbe = null)
        {
            _persistence = PortGuard.Required(persistence, nameof(persistence));
            _handler = PortGuard.Required(handler, nameof(handler));
            _initializer = initializer;
            _outcomePolicy = outcomePolicy;
            _dependencyProbe = dependencyProbe;
            options ??= new TaskPoolOptions();
            _maxConcurrency = Math.Clamp(options.MaxConcurrency, 1, 32);
            _batchSize = Math.Max(1, options.BatchSize);
            _heartbeatIntervalMs = Math.Max(0, options.HeartbeatIntervalMs);
            _drainTimeout = TimeSpan.FromMilliseconds(Math.Max(0, options.DrainTimeoutMs));
            _idleReconcile = TimeSpan.FromMilliseconds(Math.Max(0, options.IdleReconcileIntervalMs));
            _capacityWaitMs = Math.Max(0, options.CapacityWaitMs);
            _groupMaxMembers = Math.Clamp(options.GroupMaxMembers, 1, 32);
            _groupDefaultCompletion = Enum.TryParse<TaskGroupCompletion>(
                (options.GroupCompletionDefault ?? string.Empty).Replace("_", string.Empty), ignoreCase: true, out var completion)
                ? completion
                : TaskGroupCompletion.AllOf;
            _groupDefaultQuorum = Math.Max(0, options.GroupQuorumDefault);
            _allowUnverifiedGroups = options.AllowUnverifiedGroups;
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
                try
                {
                    if (_consumer is not null)
                    {
                        var budget = _capacityWaitMs > 0 ? _capacityWaitMs : 5_000;
                        await _consumer.WaitAsync(TimeSpan.FromMilliseconds(budget)).ConfigureAwait(false);
                    }
                }
                catch (TimeoutException ex)
                {
                    // 有界停机：未在预算内退出可见化（不静默丢弃）。
                    DrainFault?.Invoke(this, new TimeoutException(
                        $"TaskPool.StopAsync 有界等待超时（{_capacityWaitMs}ms）：仍有 worker 未退出。", ex));
                }
                catch { }
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
            _activeFlags[taskKey] = active;
            await _persistence.UpdatePayloadAsync(taskKey, item, ct).ConfigureAwait(false);
            if (active) await _signal.Writer.WriteAsync(true, ct).ConfigureAwait(false);
            return true;
        }

        public async Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrWhiteSpace(item.TaskKey))
                throw new ArgumentException("TaskKey cannot be empty", nameof(item));

            // U3：唯一入队初始化点（播种/视图镜像等宿主语义）；异常即 fail-fast，不落库。
            _initializer?.Initialize(item);

            var id = await _persistence.EnqueueAsync(item, ct).ConfigureAwait(false);
            _kinds[item.TaskKey] = item.Kind;
            _activeFlags[item.TaskKey] = item is IActivatableTaskPoolItem activatable ? activatable.Active : true;
            // 可重跑语义：重新入队时，仅当内存态为终态/幽灵态（Completed/Faulted/Cancelled）才重置为 NotStarted；
            // 否则 GetOrCreate 返回旧终态会导致任务不再出队（清理后重跑同 taskKey 失效）。在途（Running/Paused）由上层守卫拦截，不在此重置。
            var state = _states.GetOrCreate(item.TaskKey);
            var cur = state.CurrentState;
            if (cur is TaskExecutionStatus.Completed or TaskExecutionStatus.Faulted or TaskExecutionStatus.Cancelled)
            {
                state.TryTransitionTo(TaskExecutionStatus.NotStarted, _ => true);
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
                await TerminalizeAsync(taskKey, from, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
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
            // supervisor：唯一信号读者；worker 只从持久化取件，不读通道（保持 SingleReader 语义）。
            var workers = new List<Task>();
            try
            {
                while (await WaitSignalAsync(ct).ConfigureAwait(false))
                {
                    while (_signal.Reader.TryRead(out _)) { }

                    workers.RemoveAll(static t => t.IsCompleted);
                    while (!ct.IsCancellationRequested && workers.Count < _maxConcurrency)
                    {
                        var slot = (int)((uint)Interlocked.Increment(ref _slotSeq) % (uint)_maxConcurrency);
                        workers.Add(Task.Run(() => RunWorkerAsync(slot, ct), CancellationToken.None));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DrainFault?.Invoke(this, ex);
            }
            finally
            {
                // 优雅停机：有界等待由 StopAsync 施加；此处确保 worker 全部收尾。
                try { await Task.WhenAll(workers).ConfigureAwait(false); } catch { }
            }
        }

        /// <summary>等待唤醒：单一唤醒通道（事件信号 + 空闲兜底 tick 同源写入），返回 false = 通道已完成（应退出循环）。
        /// 空闲兜底不再经 <c>Task.WhenAny(Task.Delay)</c>（会放弃挂起读、每次迭代分配定时器），而由 <see cref="Start"/> 的定时器写入本通道。</summary>
        private async Task<bool> WaitSignalAsync(CancellationToken ct)
            => await _signal.Reader.WaitToReadAsync(ct).ConfigureAwait(false);

        private async Task RunWorkerAsync(int slot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            var skipped = 0;
            var skippedRows = new List<(string TaskKey, TaskExecutionStatus MemoryState)>();
            TItem? item = default;
            var pendingCount = 0;
            var acquired = false;

            try
            {
                // 队列级全局暂停（TCS 反应式）：暂停只挡派发，在途自然跑完。
                await DrainAsync(t => _pauseManager.WaitForResumeAsync(t), ct).ConfigureAwait(false);

                // C4：容量门先于取件/置 Running——不得出现「持久化 Running 但 handler 未进入」的假状态。
                acquired = await AcquireCapacityAsync(ct).ConfigureAwait(false);
                if (!acquired)
                {
                    sw.Stop();
                    ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                        TaskPoolConsumptionPhase.DrainFault, sw.ElapsedMilliseconds, 0, null,
                        $"capacity-wait-timeout({_capacityWaitMs}ms)", slot: slot));
                    return;
                }

                var pending = await DrainAsync(t => _persistence.ListAsync(TaskExecutionStatus.NotStarted, _batchSize, t), ct).ConfigureAwait(false);
                pendingCount = pending.Count;
                var usable = pending.Where(IsActive).ToList();
                if (usable.Count > 0)
                {
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
                }

                if (item is null)
                {
                    sw.Stop();
                    ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                        TaskPoolConsumptionPhase.DrainEnd, sw.ElapsedMilliseconds, pendingCount, null,
                        skipped: skipped, skippedRows: skippedRows, slot: slot));
                    return;
                }

                var key = item.TaskKey;
                _kinds[key] = item.Kind;
                _activeFlags[key] = item is IActivatableTaskPoolItem activatable ? activatable.Active : true;

                await DrainAsync(t => _persistence.UpdateStatusAsync(key, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running, t), ct).ConfigureAwait(false);
                lock (_lock) _inFlight.Add(key);
                Emit(key, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running, "started");

                using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeat = HeartbeatLoopAsync(key, hbCts.Token);
                var groupKey = _taskGroup.TryGetValue(key, out var g) ? g : null;
                var previousGroup = TaskGroupScope.CurrentGroupKey;
                if (groupKey is not null) TaskGroupScope.CurrentGroupKey = groupKey;
                try
                {
                    var result = await _handler(item, key, ct).ConfigureAwait(false);
                    var st = _states.Get(key);
                    var target = (_outcomePolicy ?? AlwaysCompletePolicy<TResult>.Instance).ToStatus(result);
                    if (target == TaskExecutionStatus.Completed
                        && st is { CurrentState: TaskExecutionStatus.Running }
                        && st.TryComplete())
                    {
                        await TerminalizeAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Completed, ct).ConfigureAwait(false);
                        Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Completed, "completed");
                    }
                    else if (target == TaskExecutionStatus.Faulted
                        && st is not null && st.TryFault())
                    {
                        await TerminalizeAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, ct).ConfigureAwait(false);
                        Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, "faulted_by_outcome");
                    }
                    else if (target is not (TaskExecutionStatus.Completed or TaskExecutionStatus.Faulted))
                    {
                        throw new ArgumentOutOfRangeException("outcomePolicy", target,
                            "任务终态策略只允许 Completed / Faulted。");
                    }
                }
                catch (OperationCanceledException)
                {
                    var st = _states.Get(key);
                    if (st is not null && st.TryCancel())
                    {
                        await TerminalizeAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled, ct).ConfigureAwait(false);
                        Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled, "cancelled");
                    }
                }
                catch (Exception)
                {
                    // 失败即终态（AX-ZR）：不进 Retry/NotStarted，不自动重派。
                    var st = _states.Get(key);
                    if (st is not null && st.TryFault())
                    {
                        await TerminalizeAsync(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, ct).ConfigureAwait(false);
                        Emit(key, TaskExecutionStatus.Running, TaskExecutionStatus.Faulted, "faulted");
                    }
                }
                finally
                {
                    TaskGroupScope.CurrentGroupKey = previousGroup;
                    hbCts.Cancel();
                    // 心跳纯观测：不得在消费关键路径 await；只观测异常。
                    _ = heartbeat.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }

                sw.Stop();
                ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                    TaskPoolConsumptionPhase.DrainEnd, sw.ElapsedMilliseconds, pendingCount, key,
                    skipped: skipped, skippedRows: skippedRows, slot: slot, executionId: key));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 停机取消：不记为故障。
            }
            catch (Exception ex)
            {
                sw.Stop();
                ConsumptionObserved?.Invoke(this, new TaskPoolConsumptionEventArgs(
                    TaskPoolConsumptionPhase.DrainFault, sw.ElapsedMilliseconds, pendingCount, item?.TaskKey,
                    $"{ex.GetType().Name}: {ex.Message}", slot: slot));
                DrainFault?.Invoke(this, ex);
            }
            finally
            {
                if (item is not null)
                {
                    lock (_lock) _inFlight.Remove(item.TaskKey);
                    TriggerNext();
                }
                if (acquired)
                {
                    try { _gate.Release(); }
                    catch (ObjectDisposedException) { /* 停机超时后池已释放：worker 收尾不再抛 */ }
                }
            }
        }

        /// <summary>容量门（有界等待）：超时可见化并返回 false（不无限等，也不先置 Running）。</summary>
        private async Task<bool> AcquireCapacityAsync(CancellationToken ct)
        {
            if (_capacityWaitMs <= 0)
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                return true;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_capacityWaitMs);
            try
            {
                await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
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

        /// <summary>终态单点（收敛点）：状态迁移 + 退出消费集（<see cref="IActivatableTaskPoolItem.Active"/>=false）一次完成。
        /// <para>终态任务不得再带 Active=true：否则重启回填 / 毒化对齐会把已终结的任务重新当作可消费候选
        /// （现场成员任务「Running/round=0 且 active=true」的成因之一）。</para>
        /// <para>B3b 修复：对快照驱动条目（<see cref="IStatusAwareTaskPoolItem"/>），载荷内 Status 为权威 ——
        /// 必须在写回载荷前置为终态，否则 <c>UpdatePayloadAsync</c> 会用载荷内旧状态覆盖刚写入的 Status 列
        /// （表现为终态被回滚成 NotStarted，Completed/Faulted 查询恒 0）。</para></summary>
        private async Task TerminalizeAsync(
            string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct)
        {
            await _persistence.UpdateStatusAsync(taskKey, from, to, ct).ConfigureAwait(false);

            // 以持久化最新快照为准（不拿消费期旧对象覆盖并发写入的 Failure/结果字段）。
            var latest = await _persistence.GetAsync(taskKey, ct).ConfigureAwait(false);
            if (latest is null) return;

            var dirty = false;
            if (latest is IStatusAwareTaskPoolItem statusAware && statusAware.Status != to)
            {
                statusAware.Status = to;
                dirty = true;
            }

            if (latest is IActivatableTaskPoolItem activatable && activatable.Active)
            {
                activatable.Active = false;
                dirty = true;
            }

            if (dirty) await _persistence.UpdatePayloadAsync(taskKey, latest, ct).ConfigureAwait(false);

            _activeFlags[taskKey] = false;
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

        public async Task<GroupSubmitReceipt> SubmitGroupAsync(
            TaskGroupDescriptor group, IReadOnlyList<TItem> items, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(items);

            if (string.IsNullOrWhiteSpace(group.GroupKey))
                return GroupSubmitReceipt.Rejected(group.GroupKey ?? string.Empty, "GROUP_KEY_EMPTY");
            if (group.MemberKeys is null || group.MemberKeys.Count == 0)
                return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_EMPTY");
            if (group.MemberKeys.Count > _groupMaxMembers)
                return GroupSubmitReceipt.Rejected(group.GroupKey, $"GROUP_TOO_MANY_MEMBERS({group.MemberKeys.Count}>{_groupMaxMembers})");
            if (items.Count != group.MemberKeys.Count)
                return GroupSubmitReceipt.Rejected(group.GroupKey, $"GROUP_MEMBER_COUNT_MISMATCH({items.Count}!={group.MemberKeys.Count})");
            if (group.MemberKeys.Distinct(StringComparer.Ordinal).Count() != group.MemberKeys.Count)
                return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_DUPLICATE_MEMBER");
            if (TaskGroupScope.CurrentGroupKey is not null)
                return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_NESTED");

            if (_groups.TryGetValue(group.GroupKey, out var existing))
            {
                var code = Volatile.Read(ref existing.Settled) != 0 ? "GROUP_ALREADY_SETTLED" : "GROUP_ALREADY_EXISTS";
                return new GroupSubmitReceipt(true, group.GroupKey, existing.MemberKeys.Count, code);
            }

            if (_dependencyProbe is not null)
            {
                bool hasDependency;
                try
                {
                    hasDependency = await _dependencyProbe.HasIntraGroupDependencyAsync(group.MemberKeys, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // 读取失败 ⇒ fail-closed（禁止「读不到就放行」）。
                    hasDependency = true;
                }

                if (hasDependency)
                    return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_HAS_DEPENDENCY");
            }
            else if (!_allowUnverifiedGroups)
            {
                // 探针缺失 ⇒ 默认拒收（C3 无法验证即不得成组；显式 AllowUnverifiedGroups 才放行）。
                return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_DEPENDENCY_PROBE_MISSING");
            }

            var byKey = items.GroupBy(static i => i.TaskKey, StringComparer.Ordinal)
                             .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
            foreach (var key in group.MemberKeys)
                if (!byKey.ContainsKey(key))
                    return GroupSubmitReceipt.Rejected(group.GroupKey, $"GROUP_MEMBER_NOT_FOUND:{key}");

            var state = new GroupState
            {
                Descriptor = group,
                MemberKeys = group.MemberKeys.ToArray(),
                StartedAt = DateTimeOffset.UtcNow,
            };
            if (!_groups.TryAdd(group.GroupKey, state))
                return GroupSubmitReceipt.Rejected(group.GroupKey, "GROUP_RACE_DUPLICATE");

            foreach (var key in state.MemberKeys) _taskGroup[key] = group.GroupKey;

            var enqueued = new List<string>(state.MemberKeys.Count);
            try
            {
                foreach (var key in state.MemberKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    await EnqueueAsync(byKey[key], ct).ConfigureAwait(false);
                    enqueued.Add(key);
                }
            }
            catch (Exception ex)
            {
                foreach (var key in enqueued)
                    await CancelAsync(key, CancellationToken.None).ConfigureAwait(false);
                foreach (var key in state.MemberKeys) _taskGroup.TryRemove(key, out _);
                _groups.TryRemove(group.GroupKey, out _);
                return GroupSubmitReceipt.Rejected(group.GroupKey, $"GROUP_ENQUEUE_FAILED:{ex.GetType().Name}");
            }

            return new GroupSubmitReceipt(true, group.GroupKey, state.MemberKeys.Count, null);
        }

        public async Task<TaskGroupResult> WaitGroupAsync(string groupKey, CancellationToken ct = default)
        {
            if (!_groups.TryGetValue(groupKey, out var group))
                throw new KeyNotFoundException($"GROUP_NOT_FOUND: {groupKey}");
            return await group.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public async Task<int> CancelGroupAsync(string groupKey, CancellationToken ct = default)
        {
            if (!_groups.TryGetValue(groupKey, out var group)) return 0;
            var count = 0;
            foreach (var key in group.MemberKeys)
            {
                await CancelAsync(key, ct).ConfigureAwait(false);
                count++;
            }
            return count;
        }

        private void UpdateGroupMembership(string taskKey)
        {
            if (!_taskGroup.TryGetValue(taskKey, out var groupKey)) return;
            if (!_groups.TryGetValue(groupKey, out var group)) return;
            if (Volatile.Read(ref group.Settled) != 0) return;

            var members = new List<TaskGroupMember>(group.MemberKeys.Count);
            foreach (var key in group.MemberKeys)
            {
                var memberStatus = MapStatus(_states.Get(key)?.CurrentState ?? TaskExecutionStatus.NotStarted);
                members.Add(new TaskGroupMember(key, memberStatus));
            }

            var allTerminal = members.All(static m => m.Status is TaskGroupMemberStatus.Completed
                or TaskGroupMemberStatus.Faulted or TaskGroupMemberStatus.Cancelled);
            if (!allTerminal)
            {
                if (group.Descriptor.Completion == TaskGroupCompletion.FailFast
                    && members.Any(static m => m.Status == TaskGroupMemberStatus.Faulted)
                    && Interlocked.Exchange(ref group.FailFastRequested, 1) == 0)
                {
                    var pending = members.Where(static m => m.Status is TaskGroupMemberStatus.NotStarted
                            or TaskGroupMemberStatus.Running or TaskGroupMemberStatus.Paused)
                        .Select(static m => m.TaskKey).ToArray();
                    _ = Task.WhenAll(pending.Select(k => CancelAsync(k)));
                }
                return;
            }

            if (Interlocked.Exchange(ref group.Settled, 1) != 0) return;

            var status = ResolveGroupStatus(group, members);
            var result = new TaskGroupResult(
                group.Descriptor.GroupKey,
                EffectiveCompletion(group),
                status,
                members,
                new NodeReuseStat(0, 0, 0),
                group.StartedAt,
                DateTimeOffset.UtcNow);

            foreach (var key in group.MemberKeys) _taskGroup.TryRemove(key, out _);
            group.Completion.TrySetResult(result);
        }

        private TaskGroupCompletion EffectiveCompletion(GroupState group)
            => group.Descriptor.Completion == TaskGroupCompletion.AllOf
                ? _groupDefaultCompletion
                : group.Descriptor.Completion;

        private TaskGroupMemberStatus ResolveGroupStatus(GroupState group, IReadOnlyList<TaskGroupMember> members)
        {
            var completed = members.Count(static m => m.Status == TaskGroupMemberStatus.Completed);
            var faulted = members.Any(static m => m.Status == TaskGroupMemberStatus.Faulted);
            var cancelled = members.Any(static m => m.Status == TaskGroupMemberStatus.Cancelled);

            var completion = EffectiveCompletion(group);

            switch (completion)
            {
                case TaskGroupCompletion.Quorum:
                    var quorum = group.Descriptor.Quorum > 0 ? group.Descriptor.Quorum
                        : _groupDefaultQuorum > 0 ? _groupDefaultQuorum
                        : (members.Count / 2) + 1;
                    return completed >= quorum ? TaskGroupMemberStatus.Completed : TaskGroupMemberStatus.Faulted;
                case TaskGroupCompletion.FailFast:
                case TaskGroupCompletion.AllOf:
                default:
                    if (faulted) return TaskGroupMemberStatus.Faulted;
                    if (cancelled) return TaskGroupMemberStatus.Cancelled;
                    return completed == members.Count ? TaskGroupMemberStatus.Completed : TaskGroupMemberStatus.Faulted;
            }
        }

        private static TaskGroupMemberStatus MapStatus(TaskExecutionStatus status) => status switch
        {
            TaskExecutionStatus.NotStarted => TaskGroupMemberStatus.NotStarted,
            TaskExecutionStatus.Running => TaskGroupMemberStatus.Running,
            TaskExecutionStatus.Paused => TaskGroupMemberStatus.Paused,
            TaskExecutionStatus.Completed => TaskGroupMemberStatus.Completed,
            TaskExecutionStatus.Faulted => TaskGroupMemberStatus.Faulted,
            TaskExecutionStatus.Cancelled => TaskGroupMemberStatus.Cancelled,
            _ => TaskGroupMemberStatus.NotStarted,
        };

        private sealed class GroupState
        {
            public required TaskGroupDescriptor Descriptor { get; init; }
            public required IReadOnlyList<string> MemberKeys { get; init; }
            public required DateTimeOffset StartedAt { get; init; }
            public TaskCompletionSource<TaskGroupResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Settled;
            public int FailFastRequested;
        }

        private void Emit(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, string? reason)
        {
            TaskStateChanged?.Invoke(this, new TaskPoolStateChangedEventArgs(
                taskKey, from, to, reason,
                kind: _kinds.TryGetValue(taskKey, out var kind) ? kind : TaskPoolItemKind.Executable,
                active: !_activeFlags.TryGetValue(taskKey, out var active) || active));
            UpdateGroupMembership(taskKey);
        }
    }
}

