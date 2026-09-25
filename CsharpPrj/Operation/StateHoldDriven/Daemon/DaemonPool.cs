using System.Text.Json;
using Modules.Threading;
using Operation.StateBlock.Diagnostics;
using Share.StateManagement.Pooling;

namespace Operation.StateBlock;

/// <summary>
/// 通用 daemon 池管理器（与具体工具无关）—— 单一执行通道。
/// 池内闭环完成：就绪门闩（预热/等待/补位重试）、worker 级互斥选路、预算单一时钟、
/// 错误分类（业务结果 vs 基建故障重试）、超时/挂起/进程树终止（经 AEF）。
/// 传输细节经 <see cref="IDaemonTransport"/> / <see cref="IWorkerHost"/> 注入。
/// </summary>
public sealed class DaemonPool : IDisposable
{
    private readonly DaemonPoolOptions _options;
    private readonly DaemonPoolState _state;
    private readonly IDaemonTransport _transport;
    private readonly AsyncOperationScheduler _scheduler;
    private readonly SemaphoreSlim _slotGate;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _warmupBudget;
    private readonly int _infraRetries;
    private readonly ITimingClock _clock = SystemClock.Instance;
    private readonly object _readyLock = new();
    private readonly CancellationTokenSource _poolCts = new();
    private Task<int>? _warmupTask;
    private string? _lastWarmupError; // 自旋吞错归因：上一轮 warmup 的基底异常（预算耗尽时一并带出）
    private readonly object _busyLock = new();
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);

    /// <summary>池实例标识（同一进程多池并存时用于日志归属消歧；形如 "cline#2"，序号为进程内建池顺序）。</summary>
    private readonly string _poolTag;

    /// <summary>建池序号（进程内自增，单一来源；不依赖宿主传入，故任何建池点都自带唯一标识）。</summary>
    private static int _poolSeq;

    /// <summary>池诊断钩子（宿主注入日志器；缺省 no-op）。</summary>
    public static Action<string, object?>? Diagnostics { get; set; }

    /// <summary>
    /// 池级阶段帧回调（**进程级诊断投影**；缺省 no-op）。
    /// <para>
    /// C-7：原为 <c>static</c> 可变属性 ⇒ 进程级单值承载运行期信道（多池/多任务并发串味，且静默槽门禁扫不到 Frame 层）。
    /// 现下沉为**实例**属性（每个池自己的投影）；「某次调用」的运行态事实改走 <c>SendAsync(onStage)</c> 的 per-call 通道。
    /// </para>
    /// </summary>
    public Action<string, string>? TraceSink { get; set; }

    public DaemonPool(DaemonPoolOptions options, IDaemonTransport transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var concurrency = Math.Max(1, options.PoolSize);
        _idleTimeout = options.IdleTimeout;
        _warmupBudget = options.WarmupTimeout;
        _infraRetries = Math.Max(0, options.InfraRetryLimit);
        _poolTag = $"{options.PoolName ?? "pool"}#{System.Threading.Interlocked.Increment(ref _poolSeq)}";
        _state = new DaemonPoolState(options, transport, _poolTag);
        // 实例级投影中转：状态层不持有池实例，故经闭包把状态层回调转到本实例属性（调用期求值 ⇒ 后设亦生效）。
        _state.TraceSink = (workerId, stage) => TraceSink?.Invoke(workerId, stage);
        _transport = transport;
        _scheduler = new AsyncOperationScheduler(concurrency, _idleTimeout);
        _slotGate = new SemaphoreSlim(concurrency, concurrency);
        StateBlockLog.PoolInit(_poolTag, _options.PoolSize, _idleTimeout.TotalSeconds, _warmupBudget.TotalSeconds, _infraRetries);
    }

    /// <summary>worker 状态变化（透传 WorkerPool 事件，诊断用）。</summary>
    public event Action<WorkerStateInfo>? WorkerStateChanged
    {
        add => _state.WorkerStateChanged += value;
        remove => _state.WorkerStateChanged -= value;
    }

    public PoolHealthSnapshot GetHealthSnapshot() => _state.GetHealthSnapshot();
    public bool IsReady => _state.ReadyWorkerCount > 0;
    public int ReadyWorkerCount => _state.ReadyWorkerCount;

    /// <summary>worker 当前代次（0 = 未知名单）。承载点见 B0-4 裁决 ⓐ。</summary>
    public int GenerationOf(string workerId) => _state.GenerationOf(workerId);

    /// <summary>取 worker 状态快照（含 pid 等宿主元数据；未知名单 ⇒ false）。供治理端口 <c>engine_pid</c> 契约取值。</summary>
    public bool TryGetWorkerInfo(string workerId, out WorkerStateInfo? info) => _state.TryGet(workerId, out info);

    /// <summary>
    /// **代次校验后**驱逐该 worker（复用既有基建故障清理：释放占用 + 补位重生）。
    /// 代次不符（越代 / 未知名单）⇒ 不执行并返回 false —— 治「迟到看门狗杀在飞引擎」，
    /// 即治理规则 <c>lease.generation-guard</c> 的宿主入口（唯一执行通道不变，只是加了守卫）。
    /// </summary>
    public async Task<bool> TryEvictAsync(string workerId, int generation, CancellationToken ct = default)
    {
        if (!_state.MatchesGeneration(workerId, generation))
        {
            NoteGuardRejected("evict", workerId, generation);
            return false;
        }

        await EvictWorkerAsync(workerId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// **代次校验后**终止该 worker 底层进程树（= engine_cancel 的宿主入口：唯一 killer 仍是传输层 AEF 进程句柄，
    /// 不新造执行机制）；被终止 worker 的在飞请求按既有错误分类路径以 Cancelled 收束并补位。
    /// 代次不符 ⇒ 不执行并返回 false。
    /// </summary>
    public bool TryCancel(string workerId, int generation)
    {
        if (!_state.MatchesGeneration(workerId, generation))
        {
            NoteGuardRejected("cancel", workerId, generation);
            return false;
        }

        var host = _state.GetHost(workerId);
        if (host is null) return false;

        try
        {
            host.ProcessHandle.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics?.Invoke("DaemonPool_cancel_failed", new { workerId, generation, error = ex.Message });
            return false;
        }
    }

    /// <summary>代次守卫拒绝可见化（不静默：越代操作被拒是终止事实的一部分）。</summary>
    private void NoteGuardRejected(string op, string workerId, int generation)
        => Diagnostics?.Invoke("DaemonPool_generation_guard_rejected",
            new { op, workerId, generation, current = _state.GenerationOf(workerId) });

    /// <summary>
    /// 就绪门闩：触发（幂等，用池生命周期令牌，不被任一调用方 ct 绑架）+ 等待至少一个就绪 worker；
    /// 预热失败在预算内补位重试。返回就绪 worker 数；预算耗尽抛明确归因异常（不降级到其他通道）。
    /// </summary>
    public async Task<int> EnsureReadyAsync(TimeSpan? budget = null, CancellationToken ct = default)
    {
        // 已就绪快速路径：预热已建立就绪后，请求路径复用其结果，不再重复打 EnsureReady/池就绪
        //（首次预热已记录），消除「预热 + 首个请求」两次刷屏（旧日志 EnsureReady/池就绪 各出现两次的根因）。
        // ReadyWorkerCount>0 即至少一个 Running∧Ready∧非 InFlight 的 worker，可安全直接复用。
        if (_state.ReadyWorkerCount > 0)
        {
            _state.NoteEnsureReadyFastPath();
            return _state.ReadyWorkerCount;
        }

        var effective = budget is { } b && b > TimeSpan.Zero ? b : _warmupBudget;
        var timeBudget = TimeBudget.From(effective, TimingDefaults.ClineBudgetMargin, _clock);
        var started = _clock.UtcNow;
        var warmup = TriggerWarmup(timeBudget);
        StateBlockLog.StepDebug(_poolTag, "EnsureReady", new { effectiveSec = effective.TotalSeconds, deadline = timeBudget.Deadline });
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_poolCts.Token, ct);
        timeoutCts.CancelAfter(effective);
        var token = timeoutCts.Token;
        try
        {
            while (true)
            {
                if (_state.ReadyWorkerCount > 0)
                {
                    StateBlockLog.PoolReady(_poolTag, _state.ReadyWorkerCount, (_clock.UtcNow - started).TotalSeconds);
                    return _state.ReadyWorkerCount;
                }
                if (timeBudget.IsExpired(_clock)) break; // 预算耗尽（显式归因，非取消）
                token.ThrowIfCancellationRequested();    // 外部取消/池终止
                if (warmup.IsCompleted)
                {
                    // 自旋吞错修复：吞掉上一轮 warmup 的异常，预算耗尽时随错误带出归因，而非静默重触发。
                    if (warmup.Exception is not null)
                        _lastWarmupError = warmup.Exception.GetBaseException().Message;
                    warmup = TriggerWarmup(timeBudget); // 上一轮预热失败/中断 → 清缓存重建重试
                }
                await Task.Delay(300, token).ConfigureAwait(false);
            }

            // 预算耗尽路径（ready==0 → 明确归因；先快照后清理，保留每 worker 状态/阶段供归因，避免 InformationMonotonicity 违例）
            var workers = DescribeWorkers();       // S3：KillAllDaemons 清空 _runtime 前先快照
            KillAllWorkers();                      // 后清理：清占用标记 + 杀进程树，避免泄漏孤儿进程与脏 busy 残留
            StateBlockLog.ReadyTimeoutCleanup(_poolTag, _state.ReadyWorkerCount, workers);
            throw new InvalidOperationException(
                $"daemon 池未在预算 {effective.TotalSeconds:0}s 内就绪（{_state.ReadyWorkerCount} ready，实际耗时 {(_clock.UtcNow - started).TotalSeconds:0}s）。" +
                $"各 worker 状态/阶段: {workers}。" +
                $"末次预热错误: {_lastWarmupError ?? "(无)"}。" +
                "请确认 daemon 已构建、数据目录可用、运行环境可执行。");
        }
        catch (OperationCanceledException)
        {
            if (_poolCts.IsCancellationRequested) throw; // 池已销毁，不返回误导性就绪数
            if (_state.ReadyWorkerCount > 0) return _state.ReadyWorkerCount;
            var elapsed = (_clock.UtcNow - started).TotalSeconds;
            var workers = DescribeWorkers();       // S3：先快照后清理
            KillAllWorkers();
            StateBlockLog.ReadyTimeoutCleanup(_poolTag, _state.ReadyWorkerCount, workers);
            // 归因修正：外部取消优先于预算耗尽，不再笼统报"预算 Ns"。
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(
                    $"daemon 池就绪等待被外部取消（实际耗时 {elapsed:0}s，未就绪 {_state.ReadyWorkerCount} ready）。");
            throw new InvalidOperationException(
                $"daemon 池未在预算 {effective.TotalSeconds:0}s 内就绪（{_state.ReadyWorkerCount} ready，实际耗时 {elapsed:0}s）。" +
                $"各 worker 状态/阶段: {workers}。" +
                $"末次预热错误: {_lastWarmupError ?? "(无)"}。" +
                "请确认 daemon 已构建、数据目录可用、运行环境可执行。");
        }
    }

    /// <summary>汇总各 worker 状态/阶段（就绪失败归因：Running/Ready 拆分 + 每 worker 最后 stage）。</summary>
    private string DescribeWorkers()
    {
        var items = _state.GetWorkerIds().Select(id =>
        {
            var state = _state.TryGet(id, out var info) ? info?.State.ToString() : "未知";
            var ready = _state.IsReady(id) ? "ready" : "cold";
            var stage = _state.GetLastStage(id) ?? "(无)";
            var tail = _state.GetStderrTail(id);
            return $"{id}:{state}/{ready}, stage={stage}, stderr={tail}";
        });
        return string.Join("; ", items);
    }

    private Task<int> TriggerWarmup(TimeBudget timeBudget)
    {
        lock (_readyLock)
        {
            if (_warmupTask is { IsCompleted: false })
                return _warmupTask;
            _warmupTask = WarmupCoreAsync(timeBudget, _poolCts.Token); // 只用池令牌：首调用者取消不绑架全局预热
            return _warmupTask;
        }
    }

    private async Task<int> WarmupCoreAsync(TimeBudget timeBudget, CancellationToken token)
    {
        await _state.StartAsync(token).ConfigureAwait(false);
        // 冗余预热消除：仅对未就绪（!IsReady/warm）的 worker 预热；已就绪的不再重复 ClineCore 初始化（R1）。
        // 预热对象：未就绪（Spawned/Running）或已 Dead（需先补位重 spawn）的 worker；InFlight 占用中不预热。
        var ids = _state.GetWorkerIds().Where(id => _state.NeedsWarmup(id) || _state.IsDead(id)).ToList();
        if (ids.Count == 0) return _state.ReadyWorkerCount;
        StateBlockLog.StepDebug(_poolTag, "WarmupCore_Start", new { ids, ready = _state.ReadyWorkerCount });

        // 串行预热：一次只预热一个 worker。多个工具实例并发初始化在共享数据目录上会锁竞争
        // （实测并发近乎零 CPU 阻塞死等、预算内永不完成）；首个 ready 即令就绪门闩提前返回。
        var semaphore = new SemaphoreSlim(1, 1);
        var tasks = ids.Select(async id =>
        {
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // S2 单一时钟：每次预热只用池预算的「剩余时间」，而非满额——保证 daemon 内层 watchdog
                // （budget-margin，见 ClineDaemonTransport.CreateWarmRequest）在池 deadline 之前回错误帧，
                // 归因不再被「池预算先于预热错误到期」的时钟竞速遮蔽（修复前: 末次预热错误恒为“(无)”）。
                var remaining = timeBudget.Remaining(_clock);
                if (remaining <= TimeSpan.Zero) return 0;
                await EnsureWarmableAsync(id, timeBudget, token).ConfigureAwait(false);
                // worker 级预热明细走 Debug（终端只留 池初始化/池就绪/预热汇总/请求开始完成），失败仍 Warning 显性可见。
                StateBlockLog.StepDebug(_poolTag, "WarmupCore", new { workerId = id, ok = true });
                return 1;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 归因落库：池最终抛错时带出真实预热错误（不再恒为“(无)”）。
                _lastWarmupError = ex.GetBaseException().Message;
                StateBlockLog.Fail(_poolTag, "WarmupCore", new { workerId = id, error = ex.Message });
                StateBlockLog.WarmupFailed(_poolTag, id, _state.GetLastStage(id), _state.GetStderrTail(id), ex.Message);
                try { await _state.RespawnAsync(id, token).ConfigureAwait(false); }
                catch { /* 补位失败，交由就绪门闩继续重试 */ }
                return 0;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var warmed = results.Sum();
        StateBlockLog.WarmupSummary(_poolTag, warmed, ids.Count);
        return warmed;
    }

    /// <summary>确保 worker 可预热：Dead → 先补位重 spawn（Running），再走预热。占用的 InFlight worker 不在预热列表内。</summary>
    private async Task EnsureWarmableAsync(string workerId, TimeBudget timeBudget, CancellationToken token)
    {
        if (_state.IsDead(workerId))
            await _state.RespawnAsync(workerId, token).ConfigureAwait(false);
        await WarmWorkerAsync(workerId, timeBudget, token).ConfigureAwait(false);
    }

    private async Task WarmWorkerAsync(string workerId, TimeBudget timeBudget, CancellationToken token)
    {
        var host = _state.GetHost(workerId)
            ?? throw new InvalidOperationException($"daemon '{workerId}' 不存在，无法预热");
        if (_state.IsReady(workerId) || _state.IsDead(workerId)) return; // 已就绪/已死亡：无需重复预热（R1）
        // 预热开始由 WarmSend 承载（含 workerId/requestId/aefBudgetSec，比 WarmupStart 信息更全），WarmupStart 不再重复记录。

        // 框架级强制「内层超时 < 外层预算」：AEF 绝对超时 = 池预算剩余 - margin（先于池 deadline 触发），
        // daemon 内层 watchdog = aefBudget - margin（更早）。排序: daemon < AEF < 池 deadline，归因必先于池抛错。
        var aefBudget = timeBudget.RemainingAfterMargin(_clock);
        if (aefBudget <= TimeSpan.Zero) return;
        var request = _transport.CreateWarmRequest(host, aefBudget);
        StateBlockLog.StepDebug(_poolTag, "WarmSend", new { workerId, requestId = request.Id, aefBudgetSec = aefBudget.TotalSeconds });

        // 预热与请求同机（AEF）：看门狗 + 进程树 kill + 阶段归因。
        var result = await _scheduler.RunAsync($"warm-{workerId}", async (opCt, op) =>
        {
            // TTFR：ProbeTimeout 内无任何 trace → daemon 未响应命令 → 快速失败（不再静默等满预算）。
            var resp = await host.SendAsync(request, aefBudget, opCt, () => op.ReportProgress(null), _options.ProbeTimeout ?? TimingDefaults.ClineProbe).ConfigureAwait(false);
            if (!resp.Ok || resp.Error is not null)
                throw new DaemonPoolException("warmup", resp.Error ?? $"daemon 预热 '{workerId}' 失败");
            return resp.Result;
        }, new AsyncOperationOptions
        {
            AbsoluteTimeout = aefBudget,
            // 预热空闲窗口覆盖 56s 冷启动：engine 被 ClineCore 阻塞时不发自保 stage → 空闲看门狗判挂起并归因，
            // 不再"心跳续命"静默等到 daemon watchdog 兜底。
            IdleTimeout = TimingDefaults.ClineWarmupIdle,
            Process = host.ProcessHandle,
            IsolationKey = workerId,
            ExternalToken = token,
        }).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _state.MarkReady(workerId); // S4：就绪唯一入口 = 状态机 Running → Ready
            // 预热成功由 WarmupCoreAsync 的 WarmupCore ok:true 承载，WarmupDone 与其重复，不再单独记录。
            return;
        }

        var stage = _state.GetLastStage(workerId);
        var tail = _state.GetStderrTail(workerId);
        if (IsHangPhase(result.EndPhase))
        {
            StateBlockLog.WarmupHung(_poolTag, workerId, stage, result.EndPhase.ToString(), tail, result.Error);
            throw new TimeoutException($"daemon 预热超时/挂起（阶段={stage ?? "(无)"}, stderrTail={tail}）: {result.Error}");
        }
        // 失败归因统一由 WarmupCoreAsync 的 catch 记录（DaemonPool_WarmupFailed），此处不重复打诊断（D1）。
        throw new InvalidOperationException($"daemon 预热失败（阶段={stage ?? "(无)"}, stderrTail={tail}）: {result.Error}");
    }

    /// <summary>
    /// 向池提交一个命令。唯一执行通道，无降级回退。
    /// 池内完成：预算单一时钟、就绪门闩、worker 级互斥选路、错误分类与基建重试。
    /// 业务失败抛 <see cref="DaemonPoolException"/>；基建/预算耗尽抛明确归因异常。
    /// </summary>
    /// <param name="cmd">命令名（ask/plan/resume/history）。</param>
    /// <param name="payload">命令载荷。</param>
    /// <param name="absoluteTimeout">本次调用的绝对预算（<c>null</c> = 池默认）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="onStage">**本次调用**的阶段帧回调（可选）：帧归属 = 本次调用选中的 worker（worker 级互斥 ⇒ 归属唯一），
    /// 由调用方把运行实例身份绑在上面（C-7/F-4a：不读 ambient、不按 workerId 反解）。</param>
    public async Task<JsonElement> SendAsync(string cmd, object? payload, TimeSpan? absoluteTimeout,
        CancellationToken ct = default, Action<string>? onStage = null)
    {
        var started = _clock.UtcNow;
        var deadline = DateTimeOffset.UtcNow + (absoluteTimeout ?? _options.AbsoluteTimeout);

        // 预算单一时钟：就绪等待计入预算，不再吞掉整个请求预算后冷启动叠加
        var readyBudget = Remaining(deadline);
        if (readyBudget > TimeSpan.Zero)
            await EnsureReadyAsync(readyBudget, ct).ConfigureAwait(false);

        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = Remaining(deadline);
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("daemon 请求预算耗尽");

            await _slotGate.WaitAsync(ct).ConfigureAwait(false);
            string? picked = null;
            var requestId = "";
            try
            {
                picked = await PickIdleWorkerCoreAsync(ct).ConfigureAwait(false);
                if (_state.GetHost(picked) is null || !_state.MarkBusy(picked))
                {
                    // 选路后瞬间失效（死亡/驱逐）→ 移除占用标记，重新选路
                    lock (_busyLock) _busy.Remove(picked);
                    picked = null;
                    continue;
                }
                requestId = _state.RequestTracker.Assign(picked);
                // per-call 阶段帧监听器：注册给本次调用选中的 worker（finally 注销，含重试换 worker）。
                if (onStage is not null) _state.SetStageListener(picked, onStage);
                StateBlockLog.SendStart(_poolTag, requestId, picked, cmd, remaining.TotalSeconds);

                var host = _state.GetHost(picked)!;
                var request = new DaemonRequest(requestId, cmd, payload);
                var result = await _scheduler.RunAsync(requestId, async (opCt, op) =>
                {
                    // TTFR：ProbeTimeout 内无任何 trace → daemon 未响应命令 → 快速失败。
                    var resp = await host.SendAsync(request, remaining, opCt, () => op.ReportProgress(null), _options.ProbeTimeout ?? TimingDefaults.ClineProbe).ConfigureAwait(false);
                    if (!resp.Ok || resp.Error is not null)
                        throw new DaemonPoolException(cmd, resp.Error);
                    return resp.Result is { } r ? r : throw new DaemonPoolException(cmd, "daemon 返回空结果");
                }, new AsyncOperationOptions
                {
                    AbsoluteTimeout = remaining,
                    IdleTimeout = _idleTimeout,
                    Process = host.ProcessHandle,
                    IsolationKey = picked,
                    ExternalToken = ct,
                }).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    // 显式归因：传输层 Output 为空（不应发生）→ 报业务失败，而非静默降级为 "{}" 吞掉真实结果
                    if (string.IsNullOrEmpty(result.Output))
                        throw new DaemonPoolException(cmd, "daemon 返回结果在传输层丢失（Output 为空）");
                    _state.MarkFree(picked); // InFlight → Ready，归还占用
                    StateBlockLog.SendDone(_poolTag, requestId, picked, cmd, (_clock.UtcNow - started).TotalMilliseconds);
                    return JsonSerializer.Deserialize<JsonElement>(result.Output);
                }

                // 错误分类
                if (result.EndPhase == AsyncOpPhase.Cancelled)
                {
                    StateBlockLog.SendCancelled(_poolTag, requestId, picked, cmd, result.Error);
                    _state.MarkFree(picked);             // 归还占用
                    await EvictWorkerAsync(picked, ct).ConfigureAwait(false); // 被 kill → 驱逐补位，避免选中死 worker
                    throw new OperationCanceledException(result.Error ?? "操作被取消");
                }
                if (IsHangPhase(result.EndPhase))
                {
                    // 基建故障（挂起/被杀）→ 补位；**是否重发由宿主策略裁决**（施工单 §13：无证据整轮重发 = 报错）。
                    _state.NoteTimeout(picked);           // 累计基建故障（跨补位保留）
                    var infraAttempt = ++attempt;
                    StateBlockLog.SendRetry(_poolTag, requestId, picked, cmd, infraAttempt, result.Error,
                        _state.GetLastStage(picked), _state.GetStderrTail(picked));
                    await EvictWorkerAsync(picked, ct).ConfigureAwait(false);

                    var decision = _options.InfraRetryPolicy?.Invoke(new DaemonInfraRetryContext(
                            cmd, payload, infraAttempt, _infraRetries, result.Error,
                            _state.GetLastStage(picked), _state.GetStderrTail(picked)))
                        ?? DaemonInfraRetryDecision.Reject(
                            "未注入重发策略（缺省禁止无证据整轮重放）");

                    if (!decision.Allowed)
                        throw new DaemonPoolException(cmd,
                            $"基建故障（{result.EndPhase}）且不允许重发——{decision.Reason}；"
                            + $"已尝试 {infraAttempt} 次, stderrTail={_state.GetStderrTail(picked)}");

                    Diagnostics?.Invoke("DaemonPool_infra_retry_allowed", new
                    {
                        pool = _poolTag,
                        requestId,
                        workerId = picked,
                        cmd,
                        attempt = infraAttempt,
                        resume = decision.IsResume,
                        reason = decision.Reason,
                    });
                    continue;
                }
                // Failed：daemon 正常返回业务错误（含内层任务超时）→ 不重试，映射为业务结果
                _state.MarkFree(picked); // 归还占用
                StateBlockLog.SendFailed(_poolTag, requestId, picked, cmd, result.Error,
                    _state.GetLastStage(picked), _state.GetStderrTail(picked));
                throw new DaemonPoolException(cmd, result.Error);
            }
            finally
            {
                if (picked is not null)
                {
                    _state.ClearStageListener(picked);
                    lock (_busyLock) _busy.Remove(picked);
                }
                if (requestId != "") _state.RequestTracker.Complete(requestId);
                _slotGate.Release();
            }
        }
    }

    private static TimeSpan Remaining(DateTimeOffset deadline) => deadline - DateTimeOffset.UtcNow;

    /// <summary>AEF 结束阶段是否属「挂起/超时」类基建故障（统一错误分类谓词，warmup 与请求共用）。</summary>
    private static bool IsHangPhase(AsyncOpPhase phase)
        => phase is AsyncOpPhase.TimedOut or AsyncOpPhase.IdleHung;

    /// <summary>选一个 Running∧Ready∧!Busy 的 worker（原子选路+标记占用）；全忙则背压等待，预算由外层统一计时。</summary>
    private async Task<string> PickIdleWorkerCoreAsync(CancellationToken ct)
    {
        var waited = false; // 背压等待去重：仅进入等待时打一次日志，避免 100ms 轮询刷屏
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            lock (_busyLock)
            {
                foreach (var id in _state.GetWorkerIds())
                {
                    if (_state.TryGet(id, out var info)
                        && info is { State: PoolWorkerState.Running }
                        && _state.IsReady(id)
                        && !_busy.Contains(id))
                    {
                        _busy.Add(id);
                        return id;
                    }
                }
            }

            if (_state.ReadyWorkerCount == 0)
                throw new InvalidOperationException("daemon 池无可用已就绪 worker（预热失败或全部补位中）。");
            if (!waited)
            {
                StateBlockLog.BackpressureWait(_poolTag, _state.ReadyWorkerCount);
                waited = true;
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>基建故障清理：释放占用、清就绪、补位重启该 worker（下轮选路跳过，重新预热）。</summary>
    private async Task EvictWorkerAsync(string workerId, CancellationToken ct)
    {
        lock (_busyLock) _busy.Remove(workerId);
        try { await _state.RespawnAsync(workerId, ct).ConfigureAwait(false); }
        catch (Exception ex) { StateBlockLog.RespawnFailed(_poolTag, workerId, ex.Message); /* 补位失败：下轮选路跳过 */ }
    }

    /// <summary>就绪预算耗尽/取消清理：清占用标记 + 杀全部 daemon 进程树（避免孤儿进程与脏 busy 残留）。</summary>
    private void KillAllWorkers()
    {
        lock (_busyLock) _busy.Clear();
        _state.KillAllDaemons();
    }

    public void Dispose()
    {
        StateBlockLog.PoolDispose(_poolTag);
        _poolCts.Cancel();
        _scheduler.Dispose();
        _slotGate.Dispose();
        _state.Dispose();
        _poolCts.Dispose();
    }
}
