using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **塑形器链**（通用组合子）：按序择**首个认领且产出非空**的塑形器 —— 把「声明优先、代码兜底」
/// 升为框架级组合子，供特殊计算（截断 / 折叠）挂回退而不回流引擎。
/// <para>
/// 与 <see cref="NodeGatherEngine{TGroupKey,TShape}"/> 的塑形器列表同语义：引擎本身亦按序择首个认领者；
/// 本类用于**把一组塑形器暴露为单一 <see cref="INodeShaper{TShape}"/>**（如整链作为一环，或宿主只持一个）。
/// </para>
/// </summary>
/// <typeparam name="TShape">目标形态。</typeparam>
public sealed class ChainedShaper<TShape> : INodeShaper<TShape>
{
    private readonly IReadOnlyList<INodeShaper<TShape>> _shapers;

    /// <param name="shapers">按序的塑形器（前序优先）。</param>
    public ChainedShaper(params INodeShaper<TShape>[] shapers)
    {
        ArgumentNullException.ThrowIfNull(shapers);
        _shapers = shapers;
    }

    /// <summary>任一子塑形器认领 ⇒ 认领。</summary>
    public bool Claims(string kind)
    {
        foreach (var shaper in _shapers)
        {
            if (shaper.Claims(kind))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按序取首个非空产出；全空 ⇒ <c>default</c>（该行不产出）。</summary>
    public TShape? Shape(NodeProjectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var shaper in _shapers)
        {
            var shape = shaper.Shape(item);
            if (shape is not null)
            {
                return shape;
            }
        }

        return default;
    }
}
