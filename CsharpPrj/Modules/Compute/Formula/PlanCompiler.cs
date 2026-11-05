namespace Modules.Compute.Formula;

/// <summary>
/// 算子图 → 计划：CSE 已由图建立时完成；这里做
/// <b>lattice 提升</b>（纯逐点、无跨坐标依赖的 3D 算子抬到角点域）、
/// <b>逐点融合分组</b>（同域同 tile 的连续 Recompute 链）、
/// <b>存/算决策</b>（Hash/Memory 存、Flop/Band 重算）、<b>阶段聚合</b>（按 tile 形状 + 显式依赖）。
/// <para>A 档约束：这些变换只改「在哪算/算几次」，不改任何输出表达式的<b>逐点运算顺序</b>。</para>
/// </summary>
public sealed class PlanCompiler
{
    /// <summary>重算 vs 存：哈希/内存类必须存；纯浮点/分支类重算更便宜。</summary>
    public static Materialization Decide(OperatorSpec spec) => spec.Cost switch
    {
        CostClass.Hash => Materialization.Store,
        CostClass.Memory => Materialization.Store,
        CostClass.Flop => Materialization.Recompute,
        CostClass.Band => Materialization.Recompute,
        _ => Materialization.Recompute,
    };

    /// <summary>定义域 → tile 形状（显式 Whole 优先保留）。</summary>
    public static TilePolicy TileFor(FieldShape shape, TilePolicy declared)
    {
        if (declared == TilePolicy.Whole)
            return TilePolicy.Whole;

        return shape switch
        {
            FieldShape.Voxel3D => TilePolicy.Voxel8,
            FieldShape.Column2D => TilePolicy.Column32,
            FieldShape.LatticeCell => TilePolicy.Column32,
            _ => TilePolicy.Whole,
        };
    }

    public static int TileSizeOf(TilePolicy tile) => tile switch
    {
        TilePolicy.Column32 => 32,
        TilePolicy.Voxel8 => 8,
        _ => 0,
    };

    public Plan Compile(OperatorGraph graph, TensorKey root)
    {
        ArgumentNullException.ThrowIfNull(graph);

        int rootId = graph.ProducerOf(root);
        if (rootId < 0)
            throw new InvalidOperationException($"图中不存在输出 {root} 的生产节点");

        // 1) 可达集：节点 Id 单调递增 ⇒ 升序即拓扑序
        bool[] reachable = new bool[graph.Count];
        var stack = new Stack<int>();
        stack.Push(rootId);
        reachable[rootId] = true;

        while (stack.Count > 0)
        {
            int node = stack.Pop();
            foreach (int input in graph[node].Inputs)
            {
                if (reachable[input])
                    continue;
                reachable[input] = true;
                stack.Push(input);
            }
        }

        // 2) lattice 提升：Voxel3D 且所有输入都是角点/标量 ⇒ 抬到角点域（每个单元算一次）
        FieldShape[] effective = new FieldShape[graph.Count];
        for (int id = 0; id < graph.Count; id++)
        {
            if (!reachable[id])
                continue;

            var spec = graph[id].Spec;
            var shape = spec.Shape;

            if (shape == FieldShape.Voxel3D && graph[id].Inputs.Length > 0)
            {
                bool allLatticeOrScalar = true;
                foreach (int input in graph[id].Inputs)
                {
                    if (effective[input] is not (FieldShape.LatticeCell or FieldShape.Scalar))
                    {
                        allLatticeOrScalar = false;
                        break;
                    }
                }

                if (allLatticeOrScalar)
                    shape = FieldShape.LatticeCell;
            }

            effective[id] = shape;
        }

        // 3) 节点决策 + 融合分组
        var nodes = new List<PlanNode>();
        int fusionGroup = -1;
        FieldShape lastShape = FieldShape.Scalar;
        TilePolicy lastTile = TilePolicy.Whole;

        for (int id = 0; id < graph.Count; id++)
        {
            if (!reachable[id])
                continue;

            var node = graph[id];
            TilePolicy tile = TileFor(effective[id], node.Spec.Tile);
            Materialization materialization = Decide(node.Spec);

            bool newGroup =
                effective[id] != lastShape ||
                tile != lastTile ||
                materialization == Materialization.Store ||
                fusionGroup < 0;

            if (newGroup)
                fusionGroup++;

            lastShape = effective[id];
            lastTile = tile;

            BufferSource buffer =
                id == rootId ? BufferSource.Output :
                materialization == Materialization.Store ? BufferSource.Arena :
                BufferSource.None;

            nodes.Add(new PlanNode(
                id,
                node.Spec,
                node.Inputs,
                effective[id],
                tile,
                materialization,
                buffer,
                fusionGroup));
        }

        // 4) 阶段聚合（按 tile 形状）+ 显式依赖（跨阶段的输入边）
        int[] stageOf = new int[graph.Count];
        Array.Fill(stageOf, -1);

        var stageTiles = new List<TilePolicy>();
        for (int i = 0; i < nodes.Count; i++)
        {
            TilePolicy tile = nodes[i].Tile;
            if (stageTiles.IndexOf(tile) < 0)
                stageTiles.Add(tile);
            stageOf[nodes[i].Id] = stageTiles.IndexOf(tile);
        }

        var stageNodes = new List<int>[stageTiles.Count];
        for (int i = 0; i < stageNodes.Length; i++)
            stageNodes[i] = new List<int>();

        foreach (var node in nodes)
            stageNodes[stageOf[node.Id]].Add(node.Id);

        var stageDeps = new SortedSet<int>[stageTiles.Count];
        for (int i = 0; i < stageDeps.Length; i++)
            stageDeps[i] = new SortedSet<int>();

        foreach (var node in nodes)
        {
            int stage = stageOf[node.Id];
            foreach (int input in node.Inputs)
            {
                int inputStage = stageOf[input];
                if (inputStage >= 0 && inputStage != stage)
                    stageDeps[stage].Add(inputStage);
            }
        }

        var stages = new List<PlanStage>(stageTiles.Count);
        for (int stage = 0; stage < stageTiles.Count; stage++)
        {
            var tile = stageTiles[stage];
            string name = tile switch
            {
                TilePolicy.Column32 => "column32",
                TilePolicy.Voxel8 => "voxel8",
                _ => "whole",
            };

            stages.Add(new PlanStage(
                name,
                tile,
                TileSizeOf(tile),
                stageNodes[stage].ToArray(),
                stageDeps[stage].ToArray()));
        }

        long signature = BuildSignature(graph, root, nodes);

        return new Plan(root, signature, nodes, stages);
    }

    private static long BuildSignature(OperatorGraph graph, TensorKey root, List<PlanNode> nodes)
    {
        long hash = SignatureSet.Mix(SignatureSet.Seed, root.Name);
        hash = SignatureSet.Mix(hash, nodes.Count);

        foreach (var node in nodes)
        {
            // 结构键已经包含 Kind/Shape/Tile/参数/输出名/输入键 ⇒ 直接混合即可
            hash = SignatureSet.Mix(hash, graph.KeyOf(node.Id));
        }

        return hash;
    }
}
