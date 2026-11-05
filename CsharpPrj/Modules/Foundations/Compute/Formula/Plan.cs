namespace Modules.Compute.Formula;

/// <summary>编译后的单个算子节点（含物化决策与融合分组）。</summary>
public readonly record struct PlanNode(
    int Id,
    OperatorSpec Spec,
    int[] Inputs,
    FieldShape EffectiveShape,
    TilePolicy Tile,
    Materialization Materialization,
    BufferSource Buffer,
    int FusionGroup);

/// <summary>编译后的一阶段（按 tile 形状聚合，依赖显式给出）。</summary>
public readonly record struct PlanStage(
    string Name,
    TilePolicy Tile,
    int TileSize,
    int[] NodeIds,
    int[] DependsOn);

/// <summary>
/// 编译产物：签名 + 节点表 + 阶段表。
/// <para><b>签名</b>是缓存键的一半（另一半是 tile 坐标）；签名只由结构键与参数签名构成，
/// 因此「同名同参」跨 chunk/跨会话稳定。</para>
/// </summary>
public sealed class Plan
{
    public Plan(TensorKey root, long signature, IReadOnlyList<PlanNode> nodes, IReadOnlyList<PlanStage> stages)
    {
        Root = root;
        Signature = signature;
        Nodes = nodes;
        Stages = stages;
    }

    public TensorKey Root { get; }

    public long Signature { get; }

    public IReadOnlyList<PlanNode> Nodes { get; }

    public IReadOnlyList<PlanStage> Stages { get; }

    public PlanNode? Find(int id)
    {
        foreach (var node in Nodes)
        {
            if (node.Id == id)
                return node;
        }

        return null;
    }

    /// <summary>algo: 节点 Id → 阶段序号；不存在返回 -1。</summary>
    public int StageIndexOf(int id)
    {
        for (int stage = 0; stage < Stages.Count; stage++)
        {
            foreach (int nodeId in Stages[stage].NodeIds)
            {
                if (nodeId == id)
                    return stage;
            }
        }

        return -1;
    }
}
