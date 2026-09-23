using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态来源端口 —— 运行态的**一类事实来源**（在途登记 / 持久快照 / 派生聚合…），
/// 每个来源自带 <see cref="Origin"/> 与 <see cref="SourceId"/>（可观测：来源可归因，禁匿名事实）。
/// <para>
/// 边界：本端口**只读**（不落库、不派发、不取消）；未装配 / 未命中返回 <c>null</c>
/// （对齐"未装配即诊断"模式，由上层按 <see cref="IRunStateProvider"/> 合并规则处理）。
/// </para>
/// </summary>
public interface IRunStateSource
{
    /// <summary>本来源产生的事实来源类别（决定合并优先级，见 <see cref="RunStateOrigin"/>）。</summary>
    RunStateOrigin Origin { get; }

    /// <summary>来源标识（诊断 / 归因；如 <c>governance-round-registry</c>）。</summary>
    string SourceId { get; }

    /// <summary>取单个实体的运行态；无该实体事实 → null。</summary>
    Task<LevelRunState?> GetAsync(LevelRef reference, CancellationToken ct = default);
}
