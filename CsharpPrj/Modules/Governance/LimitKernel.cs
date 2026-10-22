using System.Collections.Concurrent;

namespace Modules.Governance;

/// <summary>
/// 限制内核：资源坐标 + 作用域链 + 两相预留 + 五种裁决 + 快照（唯一计量写者）。
/// <para>
/// 并发模型：准入/提交/记账在同一内核临界区内完成，保证同一 scope+resource 的计数与裁决原子一致；
/// 观测端口在临界区外投递（观测失败就地吞，绝不阻断主路径）。
/// </para>
/// </summary>
public sealed class LimitKernel : ILimitKernel
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, LimitScope> _scopes = new(StringComparer.Ordinal);
    private readonly AsyncLocal<LimitScope?> _ambient = new();
    private readonly LimitScope _process;

    private LimitPolicyDocument _policy;
    private ILimitObserver? _observer;
    private ILimitTerminationSink? _termination;
    private IUsageEstimator? _estimator;
    private volatile bool _halted;
    private volatile string? _haltReason;

    public LimitKernel(
        LimitPolicyDocument? policy = null,
        ILimitObserver? observer = null,
        ILimitTerminationSink? termination = null,
        IUsageEstimator? estimator = null)
    {
        _policy = policy ?? LimitPolicyDocument.Default;
        _observer = observer;
        _termination = termination;
        _estimator = estimator;
        _process = new LimitScope("process", LimitScopeKind.Process, parent: null);
        _scopes[Key(LimitScopeKind.Process, _process.Id)] = _process;
    }

    /// <summary>当前策略文档（只读引用；热更新即整文档替换）。</summary>
    public LimitPolicyDocument Policy => _policy;

    /// <summary>进程根作用域快照（审计/门面 GetState 用）。</summary>
    public LimitSnapshot ProcessSnapshot => Snapshot(_process.Id);

    /// <summary>热更新策略（整文档原子替换；在途租约沿用旧规则，新调用用新规则）。</summary>
    public void Configure(LimitPolicyDocument policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        lock (_gate) _policy = policy;
    }

    /// <summary>装配端口（观测适配器在事件区就绪后挂接）。</summary>
    public void SetObserver(ILimitObserver? observer) => _observer = observer;

    public void SetTerminationSink(ILimitTerminationSink? sink) => _termination = sink;

    public void SetEstimator(IUsageEstimator? estimator) => _estimator = estimator;

    public bool IsHalted => _halted;

    public string? HaltReason => _haltReason;

    /// <summary>进入作用域（父链自动继承当前 ambient 作用域；返回句柄，Dispose 恢复前值）。</summary>
    public IDisposable Enter(LimitScopeKind kind, string id, string? parentScopeId = null)
    {
        if (kind == LimitScopeKind.Process)
            throw new ArgumentException("Process 作用域由内核持有，不可重复进入", nameof(kind));
        if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");

        LimitScope scope;
        LimitScope? previous;
        lock (_gate)
        {
            var parent = !string.IsNullOrWhiteSpace(parentScopeId) && TryFindScopeLocked(parentScopeId, out var p)
                ? p
                : _ambient.Value ?? _process;

            scope = new LimitScope(id, kind, parent);
            _scopes[Key(kind, id)] = scope;
            previous = _ambient.Value;
            _ambient.Value = scope;
        }

        return new ScopeHandle(this, scope, previous);
    }

    /// <summary>当前逻辑执行链上的作用域快照（无则 null）。</summary>
    public LimitSnapshot? CurrentSnapshot
    {
        get
        {
            var s = _ambient.Value;
            return s is null ? null : Snapshot(s.Id);
        }
    }

    public LimitDecision TryBegin(string resource, long? estimated, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var scope = ResolveScope(scopeId, out var unscoped);
        if (unscoped)
            Emit(new LimitEvent(LimitEventKind.Unscoped, resource, scope.Id, scope.Kind, estimated, null, 0, 0, null, "unscoped", 0, DateTimeOffset.UtcNow));

        LimitDecision decision;
        lock (_gate) decision = CheckLocked(scope, resource, estimated);

        EmitDecision(decision, estimated, scope, resource);
        return decision;
    }

    public ILimitLease Begin(string resource, long? estimated, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var scope = ResolveScope(scopeId, out var unscoped);
        if (unscoped)
            Emit(new LimitEvent(LimitEventKind.Unscoped, resource, scope.Id, scope.Kind, estimated, null, 0, 0, null, "unscoped", 0, DateTimeOffset.UtcNow));

        List<(LimitScope Scope, bool Gauge, long Amount)>? targets = null;
        LimitDecision decision;

        lock (_gate)
        {
            var policy = _policy;
            var amount = estimated ?? ResolveUnknownLocked(resource, scope);
            decision = CheckLocked(scope, resource, estimated);

            if (decision.IsAllowed)
            {
                targets = new List<(LimitScope, bool, long)>(6);
                foreach (var s in Chain(scope))
                {
                    var gauge = policy.RulesFor(s.Kind, resource).Any(r => r.IsGauge);
                    var c = s.GetOrAdd(resource);
                    if (gauge) c.Active++;
                    else c.Reserved += amount;
                    targets.Add((s, gauge, amount));
                }
            }
        }

        if (decision.IsAllowed)
        {
            Emit(new LimitEvent(LimitEventKind.Reserved, resource, scope.Id, scope.Kind, estimated, null,
                decision.Used, decision.Reserved, decision.Limit, decision.Reason, decision.DelayMs, DateTimeOffset.UtcNow));
            return new Lease(this, resource, decision, targets);
        }

        EmitDecision(decision, estimated, scope, resource);
        return new Lease(this, resource, decision, targets: null);
    }

    public void Record(string resource, long? actualUsage, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var scope = ResolveScope(scopeId, out var unscoped);
        if (unscoped)
            Emit(new LimitEvent(LimitEventKind.Unscoped, resource, scope.Id, scope.Kind, null, actualUsage, 0, 0, null, "unscoped", 0, DateTimeOffset.UtcNow));

        LimitDecision? tripped = null;
        LimitScope? tripScope = null;
        long used;
        long reserved;

        lock (_gate)
        {
            var chain = Chain(scope);
            var now = DateTimeOffset.UtcNow;
            foreach (var s in chain)
            {
                var c = s.GetOrAdd(resource);
                if (actualUsage is { } a)
                {
                    c.Used += a;
                    c.Samples.Add((now, a));
                    c.Trim(now);
                }
                else
                {
                    c.Unknown++;
                }
            }

            var counter = scope.TryGet(resource, out var own) ? own : new LimitScope.Counter();
            used = counter.Used;
            reserved = counter.Reserved + counter.Active;

            // 事后超限：不追罚本次（调用已完成），只把下一次准入变为 Deny/Terminate 事实。
            tripped = CheckPostRecordLocked(scope, resource, out tripScope);
        }

        Emit(new LimitEvent(LimitEventKind.Committed, resource, scope.Id, scope.Kind, null, actualUsage, used, reserved, null, null, 0, DateTimeOffset.UtcNow));

        if (tripped is not null)
        {
            var s = tripScope ?? scope;
            Emit(new LimitEvent(LimitEventKind.Tripped, resource, s.Id, s.Kind, null, actualUsage,
                tripped.Used, tripped.Reserved, tripped.Limit, tripped.Reason, 0, DateTimeOffset.UtcNow));
            if (tripped.Verdict == LimitVerdict.Terminate)
                TryTerminate(resource, s.Id, tripped.Reason);
        }
    }

    public LimitSnapshot Snapshot(string scopeId)
    {
        lock (_gate)
        {
            if (!TryFindScopeLocked(scopeId, out var scope))
                throw new InvalidOperationException($"限制作用域不存在：{scopeId}");
            return SnapshotLocked(scope);
        }
    }

    public IReadOnlyList<LimitSnapshot> Snapshots()
    {
        lock (_gate)
            return _scopes.Values
                .GroupBy(s => s.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .Select(SnapshotLocked)
                .ToArray();
    }

    public void Halt(string? reason = null)
    {
        _halted = true;
        _haltReason = reason ?? "全局限制被人工中止";
    }

    public void Reset()
    {
        lock (_gate)
        {
            _halted = false;
            _haltReason = null;
            foreach (var s in _scopes.Values) s.Clear();
        }
    }

    // ---------------- 内部 ----------------

    private LimitScope ResolveScope(string? scopeId, out bool unscoped)
    {
        unscoped = false;
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(scopeId) && TryFindScopeLocked(scopeId, out var explicitScope))
                return explicitScope;
        }

        var current = _ambient.Value;
        if (current is not null) return current;
        unscoped = true;
        return _process;
    }

    private bool TryFindScopeLocked(string scopeId, out LimitScope scope)
    {
        foreach (var s in _scopes.Values)
        {
            if (string.Equals(s.Id, scopeId, StringComparison.Ordinal))
            {
                scope = s;
                return true;
            }
        }
        scope = _process;
        return false;
    }

    private static List<LimitScope> Chain(LimitScope scope)
    {
        var list = new List<LimitScope>(6);
        for (var s = scope; s is not null; s = s.Parent) list.Add(s);
        return list;
    }

    private LimitDecision CheckLocked(LimitScope scope, string resource, long? estimated)
    {
        if (_halted)
            return LimitDecision.Blocked(LimitVerdict.Deny, resource, scope.Id, null,
                UsedOf(scope, resource), 0, _haltReason ?? "全局限制已中止", 0, scope.Kind);

        var policy = _policy;
        var amount = estimated ?? ResolveUnknownLocked(resource, scope);
        var now = DateTimeOffset.UtcNow;
        LimitDecision? lastChecked = null;

        foreach (var s in Chain(scope))
        {
            var rules = policy.RulesFor(s.Kind, resource);
            if (rules.Count == 0) continue;

            var counter = s.GetOrAdd(resource);
            counter.Trim(now);

            foreach (var rule in rules)
            {
                long used;
                long effective;
                if (rule.IsGauge)
                {
                    used = counter.Active;
                    effective = counter.Active + 1;
                    if (rule.HardLimit is { } hard && effective > hard)
                        return LimitDecision.Blocked(rule.Action, resource, s.Id, hard, used, counter.Active,
                            $"{resource}@{s.Kind} 并发占用已达上限 {hard}", rule.ThrottleDelayMs, s.Kind);
                    if (rule.SoftLimit is { } soft && effective > soft)
                        return LimitDecision.Blocked(rule.Action, resource, s.Id, soft, used, counter.Active,
                            $"{resource}@{s.Kind} 并发占用超过软上限 {soft}", rule.ThrottleDelayMs, s.Kind);
                }
                else
                {
                    used = rule.Window == LimitWindowKind.Window
                        ? counter.UsedInWindow(now, rule.WindowSeconds)
                        : counter.Used;
                    effective = used + counter.Reserved;
                    if (rule.HardLimit is { } hard && effective + amount > hard)
                        return LimitDecision.Blocked(rule.Action, resource, s.Id, hard, used, counter.Reserved,
                            $"{resource}@{s.Kind} 已达硬上限 {hard}（used={used}, reserved={counter.Reserved}）",
                            rule.ThrottleDelayMs, s.Kind);
                    if (rule.SoftLimit is { } soft && effective + amount > soft)
                        return LimitDecision.Blocked(rule.Action, resource, s.Id, soft, used, counter.Reserved,
                            $"{resource}@{s.Kind} 超过软上限 {soft}（used={used}, reserved={counter.Reserved}）",
                            rule.ThrottleDelayMs, s.Kind);
                }

                lastChecked = LimitDecision.Allow(resource, s.Id, used, counter.Reserved + counter.Active);
            }
        }

        return lastChecked ?? LimitDecision.Allow(resource, scope.Id, 0, 0);
    }

    private LimitDecision? CheckPostRecordLocked(LimitScope scope, string resource, out LimitScope? tripScope)
    {
        tripScope = null;
        var policy = _policy;
        var now = DateTimeOffset.UtcNow;
        foreach (var s in Chain(scope))
        {
            var rules = policy.RulesFor(s.Kind, resource);
            if (rules.Count == 0) continue;
            if (!s.TryGet(resource, out var counter)) continue;

            foreach (var rule in rules)
            {
                var used = rule.IsGauge ? counter.Active
                    : rule.Window == LimitWindowKind.Window ? counter.UsedInWindow(now, rule.WindowSeconds)
                    : counter.Used;
                var breachedHard = rule.HardLimit is { } hard && used > hard;
                var breachedSoft = rule.SoftLimit is { } soft && used > soft;
                if (!breachedHard && !breachedSoft) continue;

                tripScope = s;
                return LimitDecision.Blocked(rule.Action, resource, s.Id,
                    breachedHard ? rule.HardLimit : rule.SoftLimit, used, counter.Reserved,
                    $"{resource}@{s.Kind} 事后超限（used={used}）",
                    rule.ThrottleDelayMs, s.Kind);
            }
        }
        return null;
    }

    private long ResolveUnknownLocked(string resource, LimitScope scope)
    {
        var policy = _policy;
        return policy.UnknownPolicy switch
        {
            LimitUnknownPolicy.Fixed => policy.FixedEstimate,
            LimitUnknownPolicy.CountOnly => 0,
            _ => _estimator?.Estimate(resource, scope.Id) ?? 0,
        };
    }

    private static long UsedOf(LimitScope scope, string resource)
        => scope.TryGet(resource, out var c) ? c.Used : 0;

    private void EmitDecision(LimitDecision decision, long? estimated, LimitScope scope, string resource)
    {
        switch (decision.Verdict)
        {
            case LimitVerdict.Throttle:
                Emit(new LimitEvent(LimitEventKind.Throttled, resource, scope.Id, scope.Kind, estimated, null,
                    decision.Used, decision.Reserved, decision.Limit, decision.Reason, decision.DelayMs, DateTimeOffset.UtcNow));
                break;
            case LimitVerdict.Deny:
                Emit(new LimitEvent(LimitEventKind.Denied, resource, scope.Id, scope.Kind, estimated, null,
                    decision.Used, decision.Reserved, decision.Limit, decision.Reason, 0, DateTimeOffset.UtcNow));
                break;
            case LimitVerdict.Terminate:
                Emit(new LimitEvent(LimitEventKind.Denied, resource, scope.Id, scope.Kind, estimated, null,
                    decision.Used, decision.Reserved, decision.Limit, decision.Reason, 0, DateTimeOffset.UtcNow));
                Emit(new LimitEvent(LimitEventKind.Tripped, resource, scope.Id, scope.Kind, estimated, null,
                    decision.Used, decision.Reserved, decision.Limit, decision.Reason, 0, DateTimeOffset.UtcNow));
                TryTerminate(resource, decision.ScopeId, decision.Reason);
                break;
        }
    }

    private void TryTerminate(string resource, string scopeId, string reason)
    {
        try { _termination?.Terminate(resource, scopeId, reason); }
        catch { /* 终止端口失败不阻断主路径 */ }
    }

    private void Emit(LimitEvent evt)
    {
        var observer = _observer;
        if (observer is null) return;
        try { observer.Observe(evt); }
        catch { /* L8：观测失败不阻断主路径 */ }
    }

    private LimitSnapshot SnapshotLocked(LimitScope scope)
    {
        var policy = _policy;
        var resources = new List<LimitResourceSnapshot>();
        foreach (var (resource, counter) in scope.Counters())
        {
            var limit = policy.RulesFor(scope.Kind, resource)
                .Select(r => r.HardLimit ?? r.SoftLimit)
                .FirstOrDefault(v => v is > 0);
            resources.Add(new LimitResourceSnapshot(resource, counter.Used,
                counter.Reserved + counter.Active, limit, counter.Unknown));
        }
        return new LimitSnapshot(scope.Id, scope.Kind, scope.ParentId, scope.StartedAt, resources);
    }

    private static string Key(LimitScopeKind kind, string id) => $"{kind}:{id}";

    private sealed class ScopeHandle : IDisposable
    {
        private readonly LimitKernel _kernel;
        private readonly LimitScope _scope;
        private readonly LimitScope? _previous;
        private bool _disposed;

        internal ScopeHandle(LimitKernel kernel, LimitScope scope, LimitScope? previous)
        {
            _kernel = kernel;
            _scope = scope;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_kernel._gate)
            {
                _kernel._ambient.Value = _previous;
                _kernel._scopes.TryRemove(Key(_scope.Kind, _scope.Id), out _);
            }
        }
    }

    private sealed class Lease : ILimitLease
    {
        private readonly LimitKernel _kernel;
        private readonly string _resource;
        private readonly List<(LimitScope Scope, bool Gauge, long Amount)>? _targets;
        private bool _done;

        internal Lease(LimitKernel kernel, string resource, LimitDecision decision,
            List<(LimitScope Scope, bool Gauge, long Amount)>? targets)
        {
            _kernel = kernel;
            _resource = resource;
            Decision = decision;
            _targets = targets;
        }

        public LimitDecision Decision { get; }

        public void Commit(long? actualUsage)
        {
            if (_done) return;
            _done = true;
            if (_targets is null) return;
            _kernel.CommitLease(_resource, Decision.Limit, _targets, actualUsage);
        }

        public void Abort()
        {
            if (_done) return;
            _done = true;
            if (_targets is null) return;
            _kernel.AbortLease(_resource, Decision.Limit, _targets);
        }

        public void Dispose() => Abort();
    }

    private void CommitLease(string resource, long? limit, List<(LimitScope Scope, bool Gauge, long Amount)> targets, long? actual)
    {
        long used = 0, reserved = 0;
        LimitScope? first = null;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (scope, gauge, amount) in targets)
            {
                var c = scope.GetOrAdd(resource);
                if (gauge)
                {
                    if (c.Active > 0) c.Active--;
                }
                else
                {
                    c.Reserved = Math.Max(0, c.Reserved - amount);
                }

                if (actual is { } a)
                {
                    c.Used += a;
                    c.Samples.Add((now, a));
                    c.Trim(now);
                }
                else
                {
                    c.Unknown++;
                }
                first ??= scope;
            }

            if (first is not null && first.TryGet(resource, out var own))
            {
                used = own.Used;
                reserved = own.Reserved + own.Active;
            }
        }

        if (first is null) return;
        Emit(new LimitEvent(LimitEventKind.Committed, resource, first.Id, first.Kind, null, actual,
            used, reserved, limit, null, 0, DateTimeOffset.UtcNow));
    }

    private void AbortLease(string resource, long? limit, List<(LimitScope Scope, bool Gauge, long Amount)> targets)
    {
        LimitScope? first = null;
        long reserved = 0;
        lock (_gate)
        {
            foreach (var (scope, gauge, amount) in targets)
            {
                var c = scope.GetOrAdd(resource);
                if (gauge)
                {
                    if (c.Active > 0) c.Active--;
                }
                else
                {
                    c.Reserved = Math.Max(0, c.Reserved - amount);
                }
                first ??= scope;
            }
            if (first is not null && first.TryGet(resource, out var own))
                reserved = own.Reserved + own.Active;
        }

        if (first is null) return;
        Emit(new LimitEvent(LimitEventKind.Aborted, resource, first.Id, first.Kind, null, null,
            0, reserved, limit, null, 0, DateTimeOffset.UtcNow));
    }
}
