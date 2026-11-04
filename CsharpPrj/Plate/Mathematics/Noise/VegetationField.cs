namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 单个群系的植被规则（纯数据）：密度、树高与冠幅；数组顺序必须与 <see cref="BiomeField.Biomes"/> 对齐。
/// </summary>
public readonly record struct TreeRule(
    float Density,
    int HeightMinCm,
    int HeightMaxCm,
    int CanopyRadiusCm,
    int TrunkRadiusCm);

/// <summary>树体参数（纯算法数据，不含方块 ID）。</summary>
public readonly record struct VegetationSettings(
    int CellMeters,
    int MarginMeters,
    int SeedSalt,
    int SeaMarginCm)
{
    /// <summary>默认：8 m 网格、离网格边 2 m 内不落树（保证树与树之间有间隙）、海平面以上 1 m 才长树。</summary>
    public static VegetationSettings Default => new(8, 2, 0x7A3D, 100);
}

/// <summary>体素属于树的哪一部分。</summary>
public enum TreePart
{
    None,
    Trunk,
    Leaves,
}

/// <summary>
/// 规则化稀疏植被：按世界网格单元哈希决定「是否有树 / 树心 / 树高 / 冠幅」，体素按世界坐标判定归属。
/// <ul>
///   <li>跨 chunk：树心可能落在任意 chunk，冠层按世界坐标覆盖相邻 chunk；同一坐标在任何 chunk 求值一致。</li>
///   <li>确定性：全部来自 <see cref="NoisePrimitives.Hash2D"/>，不使用 <see cref="Random"/>。</li>
///   <li>性能：树placement 按网格单元缓存（线程内），每个体素只查最近的 1~4 个单元。</li>
/// </ul>
/// </summary>
public sealed class VegetationField
{
    private readonly TerrainHeightModel _model;
    private readonly TreeRule[] _rules;
    private readonly int _seed;
    private readonly int _cellCm;
    private readonly int _marginCm;
    private readonly int _seaMarginCm;
    private readonly int _maxCanopyRadiusCm;
    private readonly int _maxSearchRadiusCm;

    // 缓存只影响速度，不影响结果；key = 单元坐标，value = 该单元唯一一棵树的placement。
    private readonly ThreadLocal<Dictionary<long, TreePlacement>> _cache =
        new(() => new Dictionary<long, TreePlacement>(256));

    public VegetationField(
        TerrainHeightModel model,
        IReadOnlyList<TreeRule> rules,
        int seed,
        VegetationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rules);
        if (!model.HasBiomes)
            throw new ArgumentException("植被需要群系场（TerrainHeightModel 未注入 BiomeField）", nameof(model));
        if (rules.Count != model.BiomeCount)
            throw new ArgumentException(
                $"植被规则数 {rules.Count} 与群系数 {model.BiomeCount} 不一致", nameof(rules));
        if (settings.CellMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "植被网格必须为正");

        _model = model;
        _rules = rules.ToArray();
        _seed = seed ^ settings.SeedSalt;
        _cellCm = settings.CellMeters * model.UnitsPerMeter;
        _marginCm = settings.MarginMeters * model.UnitsPerMeter;
        _seaMarginCm = settings.SeaMarginCm;

        if (_cellCm <= 2 * _marginCm)
            throw new ArgumentOutOfRangeException(nameof(settings), "植被网格必须大于两侧留白");

        _maxCanopyRadiusCm = 0;
        foreach (var rule in _rules)
        {
            if (rule.HeightMinCm < 0 || rule.HeightMaxCm < rule.HeightMinCm)
                throw new ArgumentOutOfRangeException(nameof(rules), "树高区间非法");
            if (rule.CanopyRadiusCm < 0 || rule.TrunkRadiusCm < 0 || rule.TrunkRadiusCm > rule.CanopyRadiusCm)
                throw new ArgumentOutOfRangeException(nameof(rules), "树冠/树干半径非法");
            if (rule.CanopyRadiusCm > _maxCanopyRadiusCm)
                _maxCanopyRadiusCm = rule.CanopyRadiusCm;
        }

        _maxSearchRadiusCm = _maxCanopyRadiusCm;
    }

    /// <summary>森林高度的保守上界（cm），供高度区间缓存把树冠并入可见范围。</summary>
    public int MaxTreeTopAboveGroundCm
    {
        get
        {
            int max = 0;
            foreach (var rule in _rules)
                max = Math.Max(max, rule.HeightMaxCm + rule.CanopyRadiusCm);
            return max;
        }
    }

    public int MaxCanopyRadiusCm => _maxCanopyRadiusCm;

    /// <summary>
    /// 判定体素 (x,y,z) 是否属于某棵树的树干/树叶。
    /// <para><paramref name="groundYCm"/> 为该列地表 Y：树叶不得嵌入地形，因此要求 y &gt; groundY。</para>
    /// </summary>
    public TreePart PartAt(int x, int y, int z, int groundYCm)
    {
        if (y <= groundYCm)
            return TreePart.None;

        int cellX0 = FloorDiv(x - _maxSearchRadiusCm, _cellCm);
        int cellX1 = FloorDiv(x + _maxSearchRadiusCm, _cellCm);
        int cellZ0 = FloorDiv(z - _maxSearchRadiusCm, _cellCm);
        int cellZ1 = FloorDiv(z + _maxSearchRadiusCm, _cellCm);

        // 树干优先：先扫一遍找树干，避免同一格同时被树干与邻树树叶覆盖时判错。
        for (int cellZ = cellZ0; cellZ <= cellZ1; cellZ++)
        for (int cellX = cellX0; cellX <= cellX1; cellX++)
        {
            var tree = Placement(cellX, cellZ);
            if (!tree.Exists)
                continue;

            if (y > tree.BaseY && y <= tree.TrunkTopY)
            {
                int dx = x - tree.CenterX;
                int dz = z - tree.CenterZ;
                int radius = tree.TrunkRadiusCm;
                if (dx * dx + dz * dz <= radius * radius)
                    return TreePart.Trunk;
            }
        }

        for (int cellZ = cellZ0; cellZ <= cellZ1; cellZ++)
        for (int cellX = cellX0; cellX <= cellX1; cellX++)
        {
            var tree = Placement(cellX, cellZ);
            if (!tree.Exists)
                continue;

            int dx = x - tree.CenterX;
            int dz = z - tree.CenterZ;
            int dy = y - tree.CanopyCenterY;
            float horizontal = (dx * dx + dz * dz) / (float)(tree.CanopyRadiusCm * tree.CanopyRadiusCm);
            float vertical = (dy * dy) / (float)(tree.VerticalRadiusCm * tree.VerticalRadiusCm);
            if (horizontal + vertical <= 1f)
                return TreePart.Leaves;
        }

        return TreePart.None;
    }

    private TreePlacement Placement(int cellX, int cellZ)
    {
        long key = ((long)cellX << 32) ^ (uint)cellZ;
        var cache = _cache.Value!;
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var tree = ComputePlacement(cellX, cellZ);
        if (cache.Count > 8192)
            cache.Clear();
        cache[key] = tree;
        return tree;
    }

    /// <summary>
    /// 诊断/自检用：取出某植被网格单元里的树（不存在返回 false）。
    /// <para>与生成路径同一纯函数，仅暴露只读结果，不改变生成行为。</para>
    /// </summary>
    public bool TryGetTree(int cellX, int cellZ, out TreePlacement tree)
    {
        tree = Placement(cellX, cellZ);
        return tree.Exists;
    }

    /// <summary>植被网格边长（cm）与单元坐标口径，便于自检遍历。</summary>
    public int CellSizeCm => _cellCm;

    private TreePlacement ComputePlacement(int cellX, int cellZ)
    {
        int span = _cellCm - 2 * _marginCm;
        int centerX = cellX * _cellCm + _marginCm + (int)(Random01(cellX, cellZ, 0) * span);
        int centerZ = cellZ * _cellCm + _marginCm + (int)(Random01(cellX, cellZ, 1) * span);

        int baseY = _model.GroundYCm(centerX, centerZ);
        if (baseY <= _model.Constants.SeaLevelCm + _seaMarginCm)
            return TreePlacement.None;

        var biome = _model.SampleBiome(centerX, centerZ);
        var rule = _rules[biome.Primary];
        if (rule.Density <= 0f || Random01(cellX, cellZ, 2) >= rule.Density)
            return TreePlacement.None;

        int height = rule.HeightMinCm
            + (int)(Random01(cellX, cellZ, 3) * (rule.HeightMaxCm - rule.HeightMinCm));

        int canopyCenterY = baseY + height;
        int verticalRadius = Math.Max(1, (int)(rule.CanopyRadiusCm * 0.7f));

        return new TreePlacement(
            Exists: true,
            CenterX: centerX,
            CenterZ: centerZ,
            BaseY: baseY,
            TrunkTopY: baseY + height,
            CanopyCenterY: canopyCenterY,
            CanopyRadiusCm: rule.CanopyRadiusCm,
            VerticalRadiusCm: verticalRadius,
            TrunkRadiusCm: rule.TrunkRadiusCm);
    }

    private float Random01(int cellX, int cellZ, int channel) =>
        NoisePrimitives.HashToFloat(NoisePrimitives.Hash2D(cellX, cellZ, _seed + channel * 7919));

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        if (value % divisor != 0 && (value < 0) != (divisor < 0))
            quotient--;
        return quotient;
    }

    public readonly record struct TreePlacement(
        bool Exists,
        int CenterX,
        int CenterZ,
        int BaseY,
        int TrunkTopY,
        int CanopyCenterY,
        int CanopyRadiusCm,
        int VerticalRadiusCm,
        int TrunkRadiusCm)
    {
        public static TreePlacement None => new(false, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
