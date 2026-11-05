using Modules.Compute.Layout;
using Modules.Compute.Traversal;
using Modules.Threading;

namespace Modules.Compute.Schedule;

/// <summary>
/// 一个阶段：固定 extent + tile 边长 + 显式依赖 + 逐 tile 作业体。
/// <para><see cref="Body"/> 只在 worker 上跑；每个 tile 只写自己的输出区间 ⇒ 阶段内并行不改变结果。</para>
/// </summary>
public readonly record struct TileStage(
    string Name,
    Index3 Extent,
    int TileSize,
    int[] DependsOn,
    Action<Tile3> Body);

/// <summary>
/// tile 作业调度器：接现有 <see cref="GenerationJobScheduler"/>（代际 + 主线程定序提交 + 过期丢弃），
/// 在其上加「阶段 + 显式依赖」。
/// <ul>
///   <li>提交：<see cref="Schedule"/> 只提交依赖已满足的阶段；某阶段全部 tile 完成时自动提交后继阶段。</li>
///   <li>过期：同 <c>runKey</c> 新 <c>version</c> 覆盖旧 run，旧版本结果即使先完成也丢弃（与调度器语义一致）。</li>
///   <li>确定性：阶段内 tile 无序完成，但「各写各区」；跨阶段由依赖栅栏强制顺序。</li>
/// </ul>
/// </summary>
public sealed class TileScheduler : IDisposable
{
    private sealed class Run
    {
        public long Key;
        public long Version;
        public TileStage[] Stages;
        public int[] Remaining;
        public bool[] Started;
        public bool[] Done;
        public StageBarrier Barrier = null!;
        public readonly List<long> JobKeys = new();
        public int Errors;
    }

    private sealed record JobMeta(long RunKey, int Stage, Tile3 Tile);

    private readonly GenerationJobScheduler _jobs;
    private readonly bool _ownsJobs;
    private readonly Dictionary<long, Run> _runs = new();
    private readonly Dictionary<long, JobMeta> _jobMeta = new();
    private long _nextJobKey = 1;
    private bool _disposed;

    /// <summary>自建调度器（无窗口/无主线程派发时的直接执行语义）。</summary>
    public TileScheduler(int maxConcurrency = 0)
    {
        _jobs = new GenerationJobScheduler(null, maxConcurrency);
        _ownsJobs = true;
    }

    /// <summary>复用外部调度器（与现有 worldgen 管线共享线程配额）。</summary>
    public TileScheduler(GenerationJobScheduler jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
        _ownsJobs = false;
    }

    public int PendingCount => _jobs.PendingCount;

    public long CompletedJobs { get; private set; }

    public long FailedJobs { get; private set; }

    /// <summary>登记并启动一个 run；同 key 的旧 run 立即作废。</summary>
    public void Schedule(long runKey, long version, IReadOnlyList<TileStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ThrowIfDisposed();

        if (_runs.TryGetValue(runKey, out var previous))
        {
            if (previous.Version >= version)
                return;
            Discard(previous);
        }

        if (stages.Count == 0)
            return;

        var run = new Run
        {
            Key = runKey,
            Version = version,
            Stages = stages.ToArray(),
            Remaining = new int[stages.Count],
            Started = new bool[stages.Count],
            Done = new bool[stages.Count],
        };

        var dependencies = new StageDependency[stages.Count];
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].TileSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(stages), $"阶段 {stages[i].Name} 的 tile 边长必须为正");
            int[] deps = stages[i].DependsOn ?? Array.Empty<int>();
            foreach (int dep in deps)
            {
                if ((uint)dep >= (uint)stages.Count)
                    throw new ArgumentOutOfRangeException(nameof(stages), $"阶段 {i} 依赖越界 {dep}");
            }
            dependencies[i] = new StageDependency(i, deps);
        }

        run.Barrier = new StageBarrier(dependencies, stages.Count);
        run.Barrier.TopologicalOrder(); // 环检测前置：非法图不进入调度
        _runs[runKey] = run;

        SubmitReady(run);
    }

    /// <summary>主线程泵：先回调外部提交，再推进阶段依赖。返回本轮交付的作业数。</summary>
    public int Pump(int maxCommits, Action<JobOutcome> commit)
    {
        ThrowIfDisposed();

        int delivered = _jobs.Pump(maxCommits, outcome =>
        {
            if (outcome.Error is not null || outcome.Stale)
                FailedJobs++;

            long jobKey = outcome.Key.Key;
            if (_jobMeta.TryGetValue(jobKey, out var meta))
            {
                _jobMeta.Remove(jobKey);
                Advance(meta, outcome);
            }

            if (commit is not null)
                commit(outcome);
        });

        return delivered;
    }

    public bool IsComplete(long runKey)
    {
        var run = _runs.GetValueOrDefault(runKey);
        if (run is null)
            return false;

        foreach (bool done in run.Done)
        {
            if (!done)
                return false;
        }

        return true;
    }

    public int ErrorCount(long runKey) => _runs.GetValueOrDefault(runKey)?.Errors ?? 0;

    /// <summary>作废 run 及其全部在飞作业（版本竞态时由调用方触发）。</summary>
    public void Cancel(long runKey)
    {
        ThrowIfDisposed();

        var run = _runs.GetValueOrDefault(runKey);
        if (run is null)
            return;

        Discard(run);
    }

    public bool WaitForIdle(int timeoutMilliseconds = 5000) => _jobs.WaitForIdle(timeoutMilliseconds);

    private void Advance(JobMeta meta, JobOutcome outcome)
    {
        var run = _runs.GetValueOrDefault(meta.RunKey);
        if (run is null || run.Version != outcome.Key.Version)
            return;

        if (outcome.Error is not null)
            run.Errors++;

        CompletedJobs++;

        run.Remaining[meta.Stage]--;
        if (run.Remaining[meta.Stage] == 0)
        {
            run.Done[meta.Stage] = true;
            SubmitReady(run);
        }
    }

    private void SubmitReady(Run run)
    {
        int[] ready = run.Barrier.ReadyStages(run.Done);
        foreach (int stageIndex in ready)
        {
            if (run.Started[stageIndex])
                continue;

            run.Started[stageIndex] = true;
            var stage = run.Stages[stageIndex];
            int tiles = WorkPartition.TileCount(stage.Extent, stage.TileSize);

            if (tiles == 0)
            {
                run.Done[stageIndex] = true;
                SubmitReady(run);
                continue;
            }

            run.Remaining[stageIndex] = tiles;

            for (int ordinal = 0; ordinal < tiles; ordinal++)
            {
                Tile3 tile = WorkPartition.TileAt(stage.Extent, stage.TileSize, ordinal);
                long jobKey = _nextJobKey++;
                var body = stage.Body;

                _jobMeta[jobKey] = new JobMeta(run.Key, stageIndex, tile);
                run.JobKeys.Add(jobKey);

                _jobs.Submit(new JobKey(jobKey, run.Version), () =>
                {
                    body(tile);
                    return null;
                });
            }
        }
    }

    private void Discard(Run run)
    {
        foreach (long jobKey in run.JobKeys)
        {
            _jobMeta.Remove(jobKey);
            _jobs.Cancel(jobKey);
        }

        _runs.Remove(run.Key);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TileScheduler));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var run in _runs.Values)
            Discard(run);

        _runs.Clear();
        _jobMeta.Clear();

        if (_ownsJobs)
            _jobs.Dispose();
    }
}
