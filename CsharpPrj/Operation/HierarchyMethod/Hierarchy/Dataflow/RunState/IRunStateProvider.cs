using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 运行态读取门面（**消费方唯一入口**，SingleImplementation）—— 把"多来源（在途 / 快照 / 派生）合成一条
/// 权威运行态"的机制收敛在这里，消费方（层处理器 / UI 渲染 / 脚本）不再各自拼来源。
/// <para>
/// 合并规则唯一实现见 <see cref="MergeRunStateProvider"/>（按来源优先级字段级叠加）；
/// 未装配任何来源 → <see cref="IsAttached"/>=false 且查询返回 null（诊断透传，不伪造状态）。
/// </para>
/// </summary>
public interface IRunStateProvider
{
    /// <summary>是否已装配至少一个来源（供调用方判空诊断）。</summary>
    bool IsAttached { get; }

    /// <summary>取单个实体运行态（无事实 → null）。</summary>
    Task<LevelRunState?> GetAsync(LevelRef reference, CancellationToken ct = default);

    /// <summary>批量取运行态（键 = <see cref="LevelRef.Key"/>；无事实的实体不入字典）。</summary>
    Task<IReadOnlyDictionary<string, LevelRunState>> GetManyAsync(
        IReadOnlyList<LevelRef> references, CancellationToken ct = default);
}
