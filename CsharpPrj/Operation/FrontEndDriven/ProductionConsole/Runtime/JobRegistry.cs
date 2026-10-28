using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Runtime;

/// <summary>
/// 任务执行上下文：Runner 的进度/取消唯一入口。进度写入由注册表节流后推送，插件只调 Report。
/// </summary>
public sealed class JobContext
{
    private readonly Action<double, string> _sink;

    internal JobContext(
        string jobId,
        string capabilityId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellation,
        Action<double, string> sink)
    {
        JobId = jobId;
        CapabilityId = capabilityId;
        Parameters = parameters;
        Cancellation = cancellation;
        _sink = sink;
    }

    public string JobId { get; }

    public string CapabilityId { get; }

    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public CancellationToken Cancellation { get; }

    public double Progress { get; private set; }

    public string Phase { get; private set; } = "";

    /// <summary>上报进度（0..1）与阶段；阶段为空则保留上一次。</summary>
    public void Report(double progress, string? phase = null)
    {
        Progress = Math.Clamp(progress, 0, 1);
        if (!string.IsNullOrWhiteSpace(phase)) Phase = phase;
        _sink(Progress, Phase);
    }

    /// <summary>只推进阶段（进度不变）。</summary>
    public void ReportPhase(string phase) => Report(Progress, phase);
}

/// <summary>
/// 通用任务注册表：按 Group 串行泵执行，跨 Group 并行；提供排队位置、进度节流推送、协作式取消与终态快照。
/// <para>不感知业务：执行体由调用方以 <c>Func&lt;JobContext, Task&lt;JobExecutionResult&gt;&gt;</c> 注入
/// （插件 Runner 内部使用；框架层也可直接用于测试与批处理）。</para>
/// </summary>
public sealed class JobRegistry : IDisposable
{
    private sealed class JobRecord
    {
        public string JobId = "";
        public string CapabilityId = "";
        public string Group = "";
        public JobStatus Status = JobStatus.Queued;
        public double Progress;
        public string Phase = "";
        public string? Error;
        public string? Reason;
        public string? InputHash;
        public bool CacheHit;
        public bool Planned;
        public bool CancelRequested;
        public int QueuePosition;
        public Dictionary<string, object?> Params = new(StringComparer.Ordinal);
        public List<ArtifactSpec> Artifacts = new();
        public DateTimeOffset CreatedAt = DateTimeOffset.Now;
        public DateTimeOffset? StartedAt;
        public DateTimeOffset? FinishedAt;
        public CancellationTokenSource Cancellation = new();
        public Func<JobContext, Task<JobExecutionResult>>? Work;
    }

    private readonly object _gate = new();
    private readonly object _pushGate = new();
    private readonly Dictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _pumps = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _pushIntervalMs;
    private long _lastPushTick = long.MinValue / 2;
    private bool _disposed;

    /// <summary>任务快照变化（已按 <paramref name="pushIntervalMs"/> 节流；终态/入队强制推送）。</summary>
    public event Action<JobSnapshot>? Changed;

    public JobRegistry(int pushIntervalMs = 200)
    {
        _pushIntervalMs = Math.Max(0, pushIntervalMs);
    }

    /// <summary>提交任务：立即返回 jobId（入队后返回，不等待执行）。</summary>
    public string Submit(
        string capabilityId,
        string group,
        IReadOnlyDictionary<string, object?>? parameters,
        Func<JobContext, Task<JobExecutionResult>> work)
    {
        if (string.IsNullOrWhiteSpace(capabilityId)) throw new ArgumentException("capabilityId 为空", nameof(capabilityId));
        ArgumentNullException.ThrowIfNull(work);

        var record = new JobRecord
        {
            JobId = "job-" + Guid.NewGuid().ToString("N")[..12],
            CapabilityId = capabilityId,
            Group = string.IsNullOrWhiteSpace(group) ? "default" : group.Trim(),
            Params = parameters is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(parameters, StringComparer.Ordinal),
            Work = work,
        };

        JobSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            _jobs[record.JobId] = record;
            var queue = GetQueue(record.Group);
            queue.Add(record.JobId);
            record.QueuePosition = queue.Count;
            snapshot = Snapshot(record);
        }

        Publish(snapshot, force: true);
        StartPump(record.Group);
        return record.JobId;
    }

    public JobSnapshot? Get(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        lock (_gate)
            return _jobs.TryGetValue(jobId, out var record) ? Snapshot(record) : null;
    }

    /// <summary>最近任务（创建时间倒序）。</summary>
    public IReadOnlyList<JobSnapshot> Recent(int limit)
    {
        if (limit <= 0) return Array.Empty<JobSnapshot>();
        lock (_gate)
            return _jobs.Values
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(Snapshot)
                .ToList();
    }

    /// <summary>
    /// 移除已终态任务（产物删除后避免控制台仍显示任务卡）。排队/运行中任务拒绝移除。
    /// </summary>
    public bool Remove(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return false;

        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var record)) return false;
            if (record.Status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled))
                return false;

            _jobs.Remove(jobId);
            return true;
        }
    }

    /// <summary>取消：排队中立即生效；运行中协作式（把 CancellationToken 交给执行体）。</summary>
    public bool Cancel(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return false;
        JobSnapshot snapshot;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var record)) return false;
            if (record.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled) return false;

            if (record.Status == JobStatus.Queued)
            {
                if (_queues.TryGetValue(record.Group, out var queue))
                {
                    var index = queue.IndexOf(record.JobId);
                    if (index >= 0) queue.RemoveAt(index);
                    ReindexQueue(queue);
                }
                record.Status = JobStatus.Cancelled;
                record.Phase = "已取消";
                record.Reason = "排队中取消";
                record.FinishedAt = DateTimeOffset.Now;
            }
            else
            {
                record.CancelRequested = true;
                record.Phase = "取消中";
                try { record.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            snapshot = Snapshot(record);
        }

        Publish(snapshot, force: true);
        return true;
    }

    private void StartPump(string group)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_pumps.ContainsKey(group)) return;
            _pumps[group] = Task.Run(() => PumpAsync(group));
        }
    }

    private async Task PumpAsync(string group)
    {
        while (true)
        {
            JobRecord record;
            List<JobSnapshot> queuedUpdates = new();
            lock (_gate)
            {
                if (!_queues.TryGetValue(group, out var queue) || queue.Count == 0)
                {
                    _pumps.Remove(group);
                    return;
                }

                var jobId = queue[0];
                queue.RemoveAt(0);
                record = _jobs[jobId];
                record.Status = JobStatus.Running;
                record.StartedAt = DateTimeOffset.Now;
                record.QueuePosition = 0;
                record.Phase = "开始";
                ReindexQueue(queue);
                queuedUpdates = queue.Select(id => Snapshot(_jobs[id])).ToList();
            }

            foreach (var update in queuedUpdates) Publish(update, force: true);
            Publish(Snapshot(record), force: true);

            var context = new JobContext(
                record.JobId,
                record.CapabilityId,
                new Dictionary<string, object?>(record.Params, StringComparer.Ordinal),
                record.Cancellation.Token,
                (progress, phase) => OnProgress(record, progress, phase));

            try
            {
                var result = await record.Work!(context).ConfigureAwait(false);
                lock (_gate) ApplyResult(record, result);
            }
            catch (OperationCanceledException)
            {
                lock (_gate) ApplyCancellation(record);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    record.Status = JobStatus.Failed;
                    record.Error = $"{ex.GetType().Name}: {ex.Message}";
                    record.FinishedAt = DateTimeOffset.Now;
                    record.Phase = "异常";
                }
            }

            record.Work = null;
            record.Cancellation.Dispose();
            Publish(Snapshot(record), force: true);
        }
    }

    private void OnProgress(JobRecord record, double progress, string phase)
    {
        lock (_gate)
        {
            if (record.Status != JobStatus.Running) return;
            record.Progress = progress;
            if (!string.IsNullOrWhiteSpace(phase)) record.Phase = phase;
        }
        Publish(Snapshot(record));
    }

    private static void ApplyResult(JobRecord record, JobExecutionResult result)
    {
        record.Artifacts = result.Artifacts;
        record.InputHash = result.InputHash;
        record.CacheHit = result.CacheHit;
        record.Planned = result.Planned;
        record.Reason = result.Reason;
        record.FinishedAt = DateTimeOffset.Now;

        if (record.CancelRequested)
        {
            record.Status = JobStatus.Cancelled;
            record.Phase = "已取消";
            return;
        }

        if (result.IsSuccess)
        {
            record.Status = JobStatus.Succeeded;
            record.Progress = 1;
            record.Phase = "完成";
            return;
        }

        record.Status = JobStatus.Failed;
        record.Error = result.Error;
        record.Phase = "失败";
    }

    private static void ApplyCancellation(JobRecord record)
    {
        record.Status = JobStatus.Cancelled;
        record.Phase = "已取消";
        record.Reason ??= "运行中取消";
        record.FinishedAt = DateTimeOffset.Now;
    }

    private void ReindexQueue(List<string> queue)
    {
        for (var i = 0; i < queue.Count; i++)
            if (_jobs.TryGetValue(queue[i], out var r))
                r.QueuePosition = i + 1;
    }

    private List<string> GetQueue(string group)
    {
        if (!_queues.TryGetValue(group, out var queue))
        {
            queue = new List<string>();
            _queues[group] = queue;
        }
        return queue;
    }

    private static JobSnapshot Snapshot(JobRecord record) => new()
    {
        JobId = record.JobId,
        CapabilityId = record.CapabilityId,
        Group = record.Group,
        Status = record.Status,
        Progress = record.Progress,
        Phase = record.Phase,
        ElapsedMs = (long)((record.FinishedAt ?? DateTimeOffset.Now) - (record.StartedAt ?? record.CreatedAt)).TotalMilliseconds,
        Error = record.Error,
        Reason = record.Reason,
        Params = new Dictionary<string, object?>(record.Params, StringComparer.Ordinal),
        Artifacts = new List<ArtifactSpec>(record.Artifacts),
        InputHash = record.InputHash,
        CacheHit = record.CacheHit,
        Planned = record.Planned,
        QueuePosition = record.QueuePosition,
        CreatedAt = record.CreatedAt,
        StartedAt = record.StartedAt,
        FinishedAt = record.FinishedAt,
    };

    private void Publish(JobSnapshot snapshot, bool force = false)
    {
        var now = Environment.TickCount64;
        lock (_pushGate)
        {
            if (!force && now - _lastPushTick < _pushIntervalMs) return;
            _lastPushTick = now;
        }
        try { Changed?.Invoke(snapshot); }
        catch { /* 订阅方异常不得击穿注册表 */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JobRegistry));
    }

    public void Dispose()
    {
        List<JobRecord> running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = _jobs.Values.Where(r => r.Status == JobStatus.Running).ToList();
        }

        foreach (var record in running)
        {
            try
            {
                record.CancelRequested = true;
                record.Cancellation.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }
}
