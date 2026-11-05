namespace Modules.Compute.Schedule;

/// <summary>世界生成阶段的稳定标识（数值即拓扑序，但依赖仍以显式声明为准）。</summary>
public readonly record struct StageId(int Order)
{
    public static readonly StageId ClimateHeight = new(0);
    public static readonly StageId VoxelSections = new(1);
    public static readonly StageId Vegetation = new(2);
    public static readonly StageId Lighting = new(3);
    public static readonly StageId Meshing = new(4);
    public static readonly StageId Merge = new(5);

    public override string ToString() => $"Stage#{Order}";
}

/// <summary>一条显式依赖：<c>Stage</c> 必须在 <c>DependsOn</c> 全部完成后才能开工。</summary>
public readonly record struct StageDependency(int Stage, int[] DependsOn);

/// <summary>
/// 阶段依赖图：显式依赖 + Kahn 拓扑序 + 环检测。
/// <para>调度器按拓扑序推进；<b>不</b>用数值大小推断依赖，避免阶段编号调整时静默错序。</para>
/// </summary>
public sealed class StageBarrier
{
    private readonly int _stageCount;
    private readonly int[][] _dependsOn;

    /// <param name="dependencies">每个阶段的依赖（可乱序；缺省阶段视为无依赖）。</param>
    /// <param name="stageCount">阶段总数。</param>
    public StageBarrier(IReadOnlyList<StageDependency> dependencies, int stageCount)
    {
        if (stageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(stageCount));
        ArgumentNullException.ThrowIfNull(dependencies);

        _stageCount = stageCount;
        _dependsOn = new int[stageCount][];
        for (int i = 0; i < stageCount; i++)
            _dependsOn[i] = Array.Empty<int>();

        foreach (var dependency in dependencies)
        {
            if ((uint)dependency.Stage >= (uint)stageCount)
                throw new ArgumentOutOfRangeException(nameof(dependencies), $"阶段 {dependency.Stage} 越界（共 {stageCount}）");

            var deps = dependency.DependsOn ?? Array.Empty<int>();
            foreach (int dep in deps)
            {
                if ((uint)dep >= (uint)stageCount)
                    throw new ArgumentOutOfRangeException(nameof(dependencies), $"依赖阶段 {dep} 越界（共 {stageCount}）");
                if (dep == dependency.Stage)
                    throw new InvalidOperationException($"阶段 {dependency.Stage} 依赖自身");
            }

            _dependsOn[dependency.Stage] = deps;
        }
    }

    public int StageCount => _stageCount;

    public ReadOnlySpan<int> Dependencies(int stage)
    {
        if ((uint)stage >= (uint)_stageCount)
            throw new ArgumentOutOfRangeException(nameof(stage));
        return _dependsOn[stage];
    }

    /// <summary>Kahn 拓扑序；存在环则抛 <see cref="InvalidOperationException"/>（不静默截断）。</summary>
    public int[] TopologicalOrder()
    {
        int[] inDegree = new int[_stageCount];
        for (int stage = 0; stage < _stageCount; stage++)
        {
            foreach (int dep in _dependsOn[stage])
                inDegree[stage]++;
        }

        var ready = new Queue<int>(_stageCount);
        for (int stage = 0; stage < _stageCount; stage++)
        {
            if (inDegree[stage] == 0)
                ready.Enqueue(stage);
        }

        int[] order = new int[_stageCount];
        int emitted = 0;

        while (ready.Count > 0)
        {
            int stage = ready.Dequeue();
            order[emitted++] = stage;

            for (int other = 0; other < _stageCount; other++)
            {
                if (other == stage)
                    continue;
                foreach (int dep in _dependsOn[other])
                {
                    if (dep == stage && --inDegree[other] == 0)
                        ready.Enqueue(other);
                }
            }
        }

        if (emitted != _stageCount)
            throw new InvalidOperationException("阶段依赖存在环：无法生成拓扑序");

        return order;
    }

    /// <summary>按拓扑序反查：给定已完成集合，返回可立即开工的阶段。</summary>
    public int[] ReadyStages(ReadOnlySpan<bool> completed)
    {
        if (completed.Length != _stageCount)
            throw new ArgumentException("completed 长度必须等于阶段数", nameof(completed));

        var ready = new List<int>();
        for (int stage = 0; stage < _stageCount; stage++)
        {
            if (completed[stage])
                continue;

            bool allDone = true;
            foreach (int dep in _dependsOn[stage])
            {
                if (!completed[dep])
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
                ready.Add(stage);
        }

        return ready.ToArray();
    }
}
