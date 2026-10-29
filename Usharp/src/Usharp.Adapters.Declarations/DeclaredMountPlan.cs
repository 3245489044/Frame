using UniversalWorkflowFramework.Relations.Mounting;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// Usharp <c>[Mount]</c> 声明 → Frame 中性 <see cref="MountBinding"/>（唯一桥接）。
/// <para>与 <see cref="DeclaredRelationPlan"/> 同一次编译产出；禁读生成 JSON（两套 plan 生产路径会漂移）。</para>
/// </summary>
public static class DeclaredMountPlan
{
    /// <summary>由编译产物构建绑定表（无声明 ⇒ 空表，由调用方决定是否回退镜像）。</summary>
    public static IReadOnlyList<MountBinding> BuildFromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        return compilation.Mounts
            .Select(m => new MountBinding(
                m.MountId,
                m.Relation,
                m.Triggers,
                m.Stage,
                m.Order,
                m.Family,
                m.Coalesce,
                m.Posture,
                m.Projection))
            .ToList();
    }
}
