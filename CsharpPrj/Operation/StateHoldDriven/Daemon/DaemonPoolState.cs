using System.Collections.Concurrent;
using System.Linq;
using Modules.Threading;
using Operation.StateBlock.Diagnostics;
using Share.StateManagement.Pooling;

namespace Operation.StateBlock;

/// <summary>
/// 通用 daemon 池状态层（进程内轻量状态，不落 SQLite）：
/// 单 worker 运行期状态（State/pid/lastActivity/lastError）+ 请求关联 + 健康统计 + 生命周期编排。
/// 装配：spawn（经 <see cref="IDaemonTransport.CreateHost"/>）/ 探活 / 补位重启 / 退出清理。
/// 进程/IPC/超时在传输宿主（<see cref="IWorkerHost"/>）承载；本类承载状态与编排。
/// </summary>
public sealed class DaemonPoolState : IDisposable
{
    /// <summary>
    /// worker 生命周期状态机（S4 收敛）：单一内部状态源，替代旧的「宿主 bool 就绪 + 生命周期枚举 + _started」多处分叉。
    /// 迁移仅经 <see cref="TransitionTo"/>（迁移表校验，非法迁移拒绝）；外部经 <see cref="ToPoolState"/> 投影为
    /// <see cref="PoolWorkerState"/> 供 WorkerStateInfo / GetHealthSnapshot 上报。
    /// 阶段（lastStage）由宿主承载（S5：Node 为唯一源，C# 只读投影）。
    /// </summary>
    private enum WorkerLifecycleState
    {
        Spawned,   // 进程已登记/启动中（探活通过前）
        Running,   // 进程存活（ping 通过），尚未 warm
        Ready,     // warm 完成，可接收请求
        InFlight,  // 正在处理请求
        Dead,      // 终态（探活失败/退出/被 kill/补位前）
    }

    /// <summary>状态迁移表：唯一合法迁移路径；非法迁移在 <see cref="TransitionTo"/> 拒绝。</summary>
    private static readonly Dictionary<WorkerLifecycleState, WorkerLifecycleState[]> MigrationTable = new()
    {
        [WorkerLifecycleState.Spawned] = new[] { WorkerLifecycleState.Running, WorkerLifecycleState.Dead },
        [WorkerLifecycleState.Running] = new[] { WorkerLifecycleState.Ready, WorkerLifecycleState.Dead },
        [WorkerLifecycleState.Ready] = new[] { WorkerLifecycleState.InFlight, WorkerLifecycleState.Dead },
        [WorkerLifecycleState.InFlight] = new[] { WorkerLifecycleState.Ready, WorkerLifecycleState.Dead },
        [WorkerLifecycleState.Dead] = Array.Empty<WorkerLifecycleState>(), // 终态
    };

    /// <summary>单 worker 运行期状态（内存轻量）。阶段（lastStage）由宿主 <see cref="IWorkerHost.LastStage"/> 承载（S5 只读投影）。</summary>
    private sealed record WorkerRuntime(
        string WorkerId,
        int Pid,
        DateTime StartedAtUtc,
        DateTime LastActivityUtc,
        string? LastError,
        WorkerLifecycleState State,
        int TimeoutCount = 0);

    private readonly DaemonPoolOptions _options;
    private readonly string _poolTag;
    private const int StderrTailMax = 25; // stderr 环形缓冲上限（失败归因，防静默挂死无任何输出）
    private readonly IDaemonTransport _transport;
    private readonly RequestTracker _requestTracker = new();
    private readonly ConcurrentDictionary<string, IWorkerHost> _hosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WorkerRuntime> _runtime = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _stderrTail = new(StringComparer.Ordinal);
    // 基建故障（挂起/被杀）计数：按 workerId 累计，跨补位/重启保留（健康上报用）。
    private readonly ConcurrentDictionary<string, int> _timeoutCounts = new(StringComparer.Ordinal);
    // worker 代次（= 进程实例序号）：每次 spawn/重生 +1，初值 1；跨补位保留不随 _runtime 清理。
    // 承载点 = B0-4 裁决 ⓐ（租约/代次守卫的唯一判据来源）。
    private readonly ConcurrentDictionary<string, int> _generations = new(StringComparer.Ordinal);
    // CSG 全表日志去重：每 worker 仅首帧透传完整 CanonicalForm，后续迁移只记状态（form 主体恒定）。
    private readonly ConcurrentDictionary<string, bool> _csgFirstLogged = new(StringComparer.Ordinal);
    // 就绪门闩命中计数：池已就绪后请求路径复用（EnsureReadyAsync 快速路径）次数，聚合替代逐条日志。
    private int _ensureReadyFastPathHits;
    private bool _started;

    public IRequestTracker RequestTracker => _requestTracker;
    public int PoolSize => _options.PoolSize;

    /// <summary>该 worker 当前代次（= 进程实例序号；每次 spawn/重生 +1，初值 1）。未知名单返回 0。</summary>
    public int GenerationOf(string workerId)
        => _generations.TryGetValue(workerId, out var generation) ? generation : 0;

    /// <summary>
    /// 代次校验（lease/generation guard 的唯一判据）：名单已知 ∧ 代次相符 ⇒ true。
    /// 越代（迟到看门狗/旧租约）与未知名单一律 false —— 拒绝越代操作。
    /// </summary>
    public bool MatchesGeneration(string workerId, int generation)
        => generation > 0 && GenerationOf(workerId) == generation;

    /// <summary>worker 状态变化（透传池/诊断用）。</summary>
    public event Action<WorkerStateInfo>? WorkerStateChanged;

    public DaemonPoolState(DaemonPoolOptions options, IDaemonTransport transport, string poolTag)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _poolTag = poolTag ?? string.Empty;
    }

    // ---- 就绪语义：Running（进程起/ping 通过）与 Ready/warm（工具初始化完成）明确拆分 ----

    /// <summary>该 worker 是否已就绪（工具初始化完成，可接收请求）。唯一权威 = 状态机 <see cref="WorkerLifecycleState.Ready"/>。</summary>
    public bool IsReady(string workerId)
        => _runtime.TryGetValue(workerId, out var r) && r.State == WorkerLifecycleState.Ready;

    /// <summary>已就绪 worker 数。</summary>
    public int ReadyWorkerCount => _runtime.Values.Count(r => r.State == WorkerLifecycleState.Ready);

    /// <summary>取该 worker 最后阶段（trace 帧累计，卡点定位用）。</summary>
    public string? GetLastStage(string workerId)
        => _hosts.TryGetValue(workerId, out var h) ? h.LastStage : null;

    public IReadOnlyCollection<string> GetWorkerIds() => _runtime.Keys.ToArray();

    public IWorkerHost? GetHost(string workerId)
        => _hosts.TryGetValue(workerId, out var h) ? h : null;

    public bool TryGet(string workerId, out WorkerStateInfo? info)
    {
        if (_runtime.TryGetValue(workerId, out var r))
        {
            info = ToInfo(r);
            return true;
        }
        info = null;
        return false;
    }

    /// <summary>就绪门闩快速路径命中（池已就绪即复用，不重复打 EnsureReady/池就绪）：聚合计数替代逐条日志。</summary>
    public void NoteEnsureReadyFastPath() => Interlocked.Increment(ref _ensureReadyFastPathHits);

    /// <summary>池健康/统计快照（内部状态机 <see cref="WorkerLifecycleState"/> 投影为 <see cref="PoolWorkerState"/> 计数）。</summary>
    public PoolHealthSnapshot GetHealthSnapshot()
    {
        int starting = 0, running = 0, busy = 0, hung = 0, stopped = 0;
        foreach (var r in _runtime.Values)
        {
            switch (ToPoolState(r.State))
            {
                case PoolWorkerState.Starting: starting++; break;
                case PoolWorkerState.Running: running++; break;
                case PoolWorkerState.Busy: busy++; break;
                case PoolWorkerState.Hung: hung++; break;
                case PoolWorkerState.Stopped: stopped++; break;
            }
        }
        var total = starting + running + busy + hung + stopped;
        return new PoolHealthSnapshot(total, starting, running, busy, hung, stopped, Volatile.Read(ref _ensureReadyFastPathHits));
    }

    /// <summary>取该 worker 最近 stderr 尾部（失败归因：静默挂死也能带出 daemon 死前的最后输出；无可读输出返回“(无)”）。</summary>
    public string GetStderrTail(string workerId, int maxLines = 20)
    {
        if (!_stderrTail.TryGetValue(workerId, out var q)) return "(无)";
        var lines = q.ToArray();
        var take = lines.Skip(Math.Max(0, lines.Length - maxLines)).ToArray();
        return take.Length == 0 ? "(无)" : string.Join(" | ", take);
    }

    // ---- 编排：spawn / 探活 / 补位 / 清理 ----

    public async Task StartAsync(CancellationToken ct)
    {
        if (_started) return;
        for (var i = 0; i < _options.PoolSize; i++)
        {
            var id = $"worker-{i}";
            if (_hosts.ContainsKey(id)) continue;
            await SpawnWorkerAsync(id, ct).ConfigureAwait(false);
        }
        // 池启动明细（spawn/探活/汇总）走 Debug：终端只留 池初始化/池就绪/预热汇总/请求开始完成，失败仍 Warning 显性可见。
        StateBlockLog.StepDebug(_poolTag, "Start_Spawned", new { poolSize = _options.PoolSize, workers = GetWorkerIds() });

        // S1：探活非空超时兜底（_options.ProbeTimeout 缺省可能为 null → 无超时则 StartAsync 无限挂起）。
        var probeTimeout = _options.ProbeTimeout ?? TimingDefaults.ClineProbe;
        var hung = new List<string>();
        foreach (var id in GetWorkerIds())
        {
            var ok = _hosts.TryGetValue(id, out var h) && await h.ProbeAsync(probeTimeout, ct).ConfigureAwait(false);
            if (!ok)
            {
                // 探活仅失败时记录（成功探活由 Start_Done 汇总承载，避免每 worker 刷屏）
                StateBlockLog.Step(_poolTag, "Start_Probe", new { workerId = id, ok, timeoutSec = probeTimeout.TotalSeconds });
                MarkDead(id, "心跳探活失败（已达重试上限）");
                hung.Add(id);
            }
        }

        if (hung.Count > 0)
            throw new InvalidOperationException($"{hung.Count} 个 worker 心跳探活失败: {string.Join(", ", hung)}");
        _started = true;
        StateBlockLog.StepDebug(_poolTag, "Start_Done", new { running = GetWorkerIds(), ready = ReadyWorkerCount });
    }

    /// <summary>补位/重启单个 daemon（被 kill/挂起后）。</summary>
    public async Task RespawnAsync(string workerId, CancellationToken ct)
    {
        StateBlockLog.Respawn(_poolTag, workerId);
        if (_hosts.TryRemove(workerId, out var old)) old.Dispose();
        _runtime.TryRemove(workerId, out _); // 新 worker 未就绪，需重新预热
        _stderrTail.TryRemove(workerId, out _); // 清旧 stderr，避免归因串脏
        await SpawnWorkerAsync(workerId, ct).ConfigureAwait(false); // SpawnWorkerAsync 内部 SetPid 已置 Running，无需再 MarkRunning（R7）
    }

    /// <summary>就绪预算耗尽/取消时清理：杀掉全部 daemon 进程树并清状态，避免孤儿进程残留；池可再次 StartAsync 重建。</summary>
    public void KillAllDaemons()
    {
        foreach (var id in _hosts.Keys.ToList())
        {
            if (_hosts.TryRemove(id, out var h)) h.Dispose(); // Dispose 杀整棵进程树
        }
        _runtime.Clear();
        _stderrTail.Clear();
        _timeoutCounts.Clear();
        _started = false;
    }

    // ---- 内部 ----

    private async Task SpawnWorkerAsync(string workerId, CancellationToken ct)
    {
        _runtime.TryRemove(workerId, out _); // 清理残留（Dead/半初始化），确保从 Spawned 干净重建
        _csgFirstLogged.TryRemove(workerId, out _); // 新进程实例重新允许透传一次完整 CSG form 基线
        // 代次 +1（首次 = 1）：每次 spawn 即新一代实例；迟到的越代操作据此被拒（B0-4 裁决 ⓐ）。
        _generations.AddOrUpdate(workerId, 1, static (_, g) => g + 1);
        Add(workerId);

        var host = _transport.CreateHost(workerId);
        // stderr（--trace/诊断）转发到池诊断日志，定位 daemon 卡点；并环形缓冲尾部供失败归因。
        host.Stderr += line =>
        {
            if (line is not null)
            {
                var q = _stderrTail.GetOrAdd(workerId, _ => new ConcurrentQueue<string>());
                q.Enqueue(line);
                while (q.Count > StderrTailMax && q.TryDequeue(out _)) { }
            }
            StateBlockLog.DaemonStderr(_poolTag, workerId, line);
        };
        // 阶段追踪（观测默认开）：trace 协议帧 → 透传诊断（lastStage 由宿主自行累计）。
        host.Trace += resp => OnTrace(workerId, resp);

        await host.StartAsync(ct).ConfigureAwait(false);
        _hosts[workerId] = host;
        host.Exited += () => OnHostExited(workerId);

        SetPid(workerId, host.ProcessHandle.Id);
        // Start_Spawn 逐 worker 细节由 Start_Spawned 汇总承载（poolSize+workers），此处不再单独记录。
    }

    private void OnTrace(string workerId, DaemonResponse resp)
    {
        if (string.IsNullOrWhiteSpace(resp.Stage)) return;
        if (string.Equals(resp.Type, "csg", StringComparison.Ordinal))
        {
            // CSG 状态帧（type:"csg"）：首帧透传完整 CanonicalForm 契约载荷，供 C# 侧消费/核对 worker 生命周期状态；
            // 后续迁移 form 主体（nodes/edges/metadata）恒定，仅记迁移状态，避免整表刷屏。
            if (_csgFirstLogged.TryAdd(workerId, true))
                StateBlockLog.DaemonCsg(_poolTag, workerId, resp.Stage, resp.Form);
            else
                StateBlockLog.DaemonCsgTransition(_poolTag, workerId, resp.Stage);
            return;
        }
        StateBlockLog.DaemonTrace(_poolTag, workerId, resp.Stage, resp.Detail);
    }

    private void OnHostExited(string workerId)
    {
        StateBlockLog.WorkerExited(_poolTag, workerId);
        _hosts.TryRemove(workerId, out _);
        TransitionTo(workerId, WorkerLifecycleState.Dead);
    }

    // ---- 状态字典辅助（状态迁移仅经 TransitionTo 迁移表校验） ----

    private void Add(string workerId)
    {
        if (_runtime.ContainsKey(workerId)) return;
        var now = DateTime.UtcNow;
        var r = new WorkerRuntime(workerId, 0, now, now, null, WorkerLifecycleState.Spawned);
        if (_runtime.TryAdd(workerId, r))
            WorkerStateChanged?.Invoke(ToInfo(r));
    }

    private void SetPid(string workerId, int pid)
    {
        TransitionTo(workerId, WorkerLifecycleState.Running);   // Spawned → Running
        Update(workerId, r => r with { Pid = pid, LastActivityUtc = DateTime.UtcNow });
    }

    /// <summary>标记就绪（warm）：预热请求成功后由池调用，使 <see cref="IsReady"/> 为 true（Running → Ready）。</summary>
    public void MarkReady(string workerId)
        => TransitionTo(workerId, WorkerLifecycleState.Ready);

    /// <summary>占用（请求进）：Ready → InFlight。失败（worker 已死亡/被驱逐）返回 false，调用方应重新选路。</summary>
    public bool MarkBusy(string workerId) => TryTransitionTo(workerId, WorkerLifecycleState.InFlight);

    /// <summary>释放（请求出）：InFlight → Ready。worker 已死亡/被驱逐时忽略（OnHostExited/Respawn 已接管）。</summary>
    public bool MarkFree(string workerId) => TryTransitionTo(workerId, WorkerLifecycleState.Ready);

    /// <summary>是否需要预热（Spawned/Running 且尚未 warm）；InFlight/Ready/Dead 无需。</summary>
    public bool NeedsWarmup(string workerId)
        => _runtime.TryGetValue(workerId, out var r)
           && r.State is WorkerLifecycleState.Spawned or WorkerLifecycleState.Running;

    /// <summary>是否已死亡（终态，需补位重 spawn 才能复用）。</summary>
    public bool IsDead(string workerId)
        => _runtime.TryGetValue(workerId, out var r) && r.State == WorkerLifecycleState.Dead;

    /// <summary>记录一次基建故障（挂起/被杀）：按 workerId 累计，跨补位/重启保留，供健康上报。</summary>
    public void NoteTimeout(string workerId)
        => _timeoutCounts.AddOrUpdate(workerId, 1, (_, c) => c + 1);

    /// <summary>该 worker 累计基建故障次数（健康上报）。</summary>
    public int GetTimeoutCount(string workerId)
        => _timeoutCounts.TryGetValue(workerId, out var c) ? c : 0;

    private void MarkDead(string workerId, string? error)
    {
        // 清理宿主并杀进程树（否则重试时残留孤儿进程，且 StartAsync 因 ContainsKey 跳过补位）。
        if (_hosts.TryRemove(workerId, out var h)) h.Dispose();
        TryTransitionTo(workerId, WorkerLifecycleState.Dead);   // 任意非终态 → Dead（已 Dead 容忍忽略）
        Update(workerId, r => r with { LastError = error });
    }

    /// <summary>状态迁移唯一入口：迁移表校验，非法迁移拒绝（抛异常，状态不变）。</summary>
    private void TransitionTo(string workerId, WorkerLifecycleState to)
    {
        Update(workerId, r =>
        {
            if (!MigrationTable.TryGetValue(r.State, out var allowed) || !allowed.Contains(to))
            {
                StateBlockLog.IllegalTransition(_poolTag, workerId, r.State.ToString(), to.ToString());
                throw new InvalidOperationException(
                    $"非法 worker 状态迁移: {r.State} → {to}（worker={workerId}）");
            }
            return r with { State = to, LastActivityUtc = DateTime.UtcNow };
        });
    }

    /// <summary>容忍竞态的迁移：worker 已死亡/被驱逐时忽略（不抛 IllegalTransition），保证请求生命周期与 OnHostExited/Respawn 安全交错。</summary>
    private bool TryTransitionTo(string workerId, WorkerLifecycleState to)
    {
        try
        {
            if (!_runtime.TryGetValue(workerId, out var r)) return false;
            if (r.State == to) return true;
            if (!MigrationTable.TryGetValue(r.State, out var allowed) || !allowed.Contains(to)) return false;
            TransitionTo(workerId, to);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // 竞态：OnHostExited/Respawn 已置 Dead/移除
        }
    }

    private void Update(string workerId, Func<WorkerRuntime, WorkerRuntime> apply)
    {
        if (!_runtime.TryGetValue(workerId, out var current))
            return;
        var updated = apply(current);
        if (_runtime.TryUpdate(workerId, updated, current))
            WorkerStateChanged?.Invoke(ToInfo(updated));
    }

    private WorkerStateInfo ToInfo(WorkerRuntime r)
        => new(r.WorkerId, ToPoolState(r.State), r.Pid, r.StartedAtUtc, r.LastActivityUtc, r.LastError, GetTimeoutCount(r.WorkerId));

    /// <summary>内部状态机 → 外部 <see cref="PoolWorkerState"/> 投影（Reporting 契约不变）。</summary>
    private static PoolWorkerState ToPoolState(WorkerLifecycleState s) => s switch
    {
        WorkerLifecycleState.Spawned => PoolWorkerState.Starting,
        WorkerLifecycleState.Running => PoolWorkerState.Running,
        WorkerLifecycleState.Ready => PoolWorkerState.Running,
        WorkerLifecycleState.InFlight => PoolWorkerState.Busy,
        WorkerLifecycleState.Dead => PoolWorkerState.Stopped,
        _ => PoolWorkerState.Starting,
    };

    public void Dispose()
    {
        foreach (var id in _hosts.Keys.ToList())
        {
            if (_hosts.TryRemove(id, out var h)) h.Dispose();
        }
        _requestTracker.Dispose();
        _runtime.Clear();
        _timeoutCounts.Clear();
    }
}

