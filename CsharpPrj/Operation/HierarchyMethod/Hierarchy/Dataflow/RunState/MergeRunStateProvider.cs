using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态合并门面（<see cref="IRunStateProvider"/> 的默认实现）—— 按来源优先级做**字段级叠加**
/// （低优先级 = 底，高优先级非空字段覆盖），等价于逻辑层 <c>Resolve(table)</c> 的"表驱动择一 + 末路完备"：
/// 无任何来源命中 → null（不伪造 Idle 冒充事实）。
/// <para>
/// 优先级由 <see cref="IRunStateSource.Origin"/> 给出（Derived &lt; Snapshot &lt; Live），装配期一次性排序、
/// 运行期只读；新增来源 = 注册一个实现，合并/消费方零改动（对齐"开放词表 + 单一合并实现"）。
/// </para>
/// <para>只读门面：无缓存、无写路径、无副作用（每次查询即取当下事实）。</para>
/// </summary>
public sealed class MergeRunStateProvider : IRunStateProvider
{
    private readonly IReadOnlyList<IRunStateSource> _sources;

    /// <summary>未装配来源的空门面（查询恒 null；<see cref="IsAttached"/>=false）。</summary>
    public static IRunStateProvider Empty { get; } = new MergeRunStateProvider(Array.Empty<IRunStateSource>());

    /// <summary>以升序优先级装配（低优先级在前，高优先级后覆盖）。</summary>
    public MergeRunStateProvider(IReadOnlyList<IRunStateSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources
            .Where(static s => s is not null)
            .OrderBy(static s => (int)s.Origin)
            .ToList();
    }

    /// <summary>便捷构造：过滤 null 后按 <see cref="RunStateOrigin"/> 升序装配。</summary>
    public static MergeRunStateProvider Create(params IRunStateSource?[] sources)
        => new((sources ?? Array.Empty<IRunStateSource?>()).Where(static s => s is not null).Select(s => s!).ToList());

    /// <summary>已装配来源数。</summary>
    public int SourceCount => _sources.Count;

    /// <summary>已装配来源标识（诊断：谁提供了运行态事实）。</summary>
    public IReadOnlyList<string> SourceIds => _sources.Select(static s => s.SourceId).ToList();

    /// <inheritdoc />
    public bool IsAttached => _sources.Count > 0;

    /// <inheritdoc />
    public async Task<LevelRunState?> GetAsync(LevelRef reference, CancellationToken ct = default)
    {
        if (_sources.Count == 0 || reference.IsEmpty) return null;

        LevelRunState? merged = null;
        foreach (var source in _sources)
        {
            var state = await source.GetAsync(reference, ct).ConfigureAwait(false);
            merged = LevelRunState.Merge(merged, state);
        }
        return merged;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, LevelRunState>> GetManyAsync(
        IReadOnlyList<LevelRef> references, CancellationToken ct = default)
    {
        var result = new Dictionary<string, LevelRunState>(StringComparer.Ordinal);
        if (_sources.Count == 0 || references is null || references.Count == 0) return result;

        foreach (var reference in references)
        {
            if (reference.IsEmpty) continue;
            var state = await GetAsync(reference, ct).ConfigureAwait(false);
            if (state is not null) result[reference.Key] = state;
        }
        return result;
    }
}
