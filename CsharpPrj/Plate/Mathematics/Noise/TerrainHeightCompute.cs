using Modules.Compute.Adapters;
using Modules.Compute.Formula;
using Modules.Compute.Layout;
using Modules.Compute.Traversal;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 地形高度的批量求值器：把 <see cref="TerrainHeightModel.HeightCm"/> 的逐点闭式搬到通用计算基底上，
/// 但<b>逐点复现同一运算顺序</b>（A 档：搬前搬后逐位一致）。
///
/// <para><b>不改变的东西（逐位一致的前提）：</b></para>
/// <list type="bullet">
///   <item>lattice 角值：<c>HashToFloat(Hash2D(x0,z0,seed)) * 2f - 1f</c>（与 <c>TerrainNoise</c> 逐字同式）。</item>
///   <item>格号：<c>(int)MathF.Floor(value/(float)divisor)</c>（不改整数除法）。</item>
///   <item>格内坐标：<c>(x - x0*cell) / (float)cell</c>（<b>除法</b>，绝不换成乘逆元）。</item>
///   <item>插值：<c>a = h00 + (h10-h00)*u; b = h01 + (h11-h01)*u; r = a + (b-a)*v</c>（同序）。</item>
///   <item>三倍频叠加：<c>0.62f*A + 0.28f*B + 0.10f*C</c>（左结合，不重排）。</item>
///   <item>高度公式：无群系 <c>HeightOffset + noise*HeightScale</c>；有群系走 <see cref="HeightFormula.Evaluate"/>。</item>
///   <item>取整：<c>(int)MathF.Round(...)</c>（银行家舍入，不改 <c>Math.Round(double)</c>）。</item>
/// </list>
///
/// <para><b>改变的东西（只减少重复计算，不改变数值）：</b></para>
/// <list type="bullet">
///   <item>角值按 lattice 单元求一次（每 tile 每倍频 ≤ 若干次），用 <see cref="TileCache"/> 跨 chunk 复用。</item>
///   <item>遍历按 <see cref="TileCursor3"/> 的 32×32 列 tile（输出各写各 tile，外加无浮点归约）。</item>
///   <item>运算通过 <see cref="Plan"/> 描述（算子图 → CSE/lattice 提升/存算决策），便于后续阶段复用同一签名。</item>
/// </list>
///
/// <para><b>线程约定</b>：实例无可变 scratch（每次 fill 自建局部缓冲），<see cref="TileCache"/> 并发安全 ⇒ 可跨 worker 共享。
/// 但 <see cref="FillHeightCm"/> 内的临时缓冲按调用分配，P4 会换成 <c>Arena</c>。</para>
/// </summary>
public sealed class TerrainHeightBatchEvaluator
{
    private const int OctaveCount = 3;
    private const int TileSize = 32;

    private readonly TerrainHeightModel _model;
    private readonly int _seed;
    private readonly int _unitsPerMeter;
    private readonly TerrainConstants _constants;
    private readonly IReadOnlyList<BiomeClimate>? _biomes;
    private readonly int[] _cells;
    private readonly long _signature;

    private readonly Plan _plan;
    private readonly TileCache _lattice;

    public TerrainHeightBatchEvaluator(TerrainHeightModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        _seed = model.Seed;
        _unitsPerMeter = model.UnitsPerMeter;
        _constants = model.Constants;
        _biomes = model.Biomes;
        _cells = new[] { 16 * _unitsPerMeter, 8 * _unitsPerMeter, 4 * _unitsPerMeter };
        _signature = model.Signature;

        _plan = CompilePlan();
        _lattice = new TileCache();
    }

    /// <summary>编译后的计划（签名 + 阶段 + 存算决策）；用于诊断与缓存键。</summary>
    public Plan Plan => _plan;

    /// <summary>lattice 角值缓存（跨 chunk 复用同一角点）。</summary>
    public TileCache LatticeCache => _lattice;

    /// <summary>按 32×32 列 tile 批量求 <c>HeightCm</c>（x 最快行主序，偏移见 <see cref="GridRect.Offset"/>）。</summary>
    public void FillHeightCm(GridRect rect, Span<float> dst)
    {
        if (rect.IsEmpty)
            return;
        if (dst.Length < rect.Count)
            throw new ArgumentException($"目标缓冲太短：需要 {rect.Count}，实际 {dst.Length}", nameof(dst));

        var extent = Index3.XFastest(rect.Width, 1, rect.Height);
        var cursor = new TileCursor3(extent, TileSize, 1, TileSize);
        var scratch = new TileScratch(TileSize);

        while (cursor.MoveNext())
            FillHeightTile(rect, cursor.Current, dst, scratch);
    }

    /// <summary>批量求 <c>GroundYCm</c>：先批量高度，再逐点 <c>(int)MathF.Round</c>（与单点逐位一致）。</summary>
    public void FillGroundYCm(GridRect rect, Span<int> dst)
    {
        if (rect.IsEmpty)
            return;
        if (dst.Length < rect.Count)
            throw new ArgumentException($"目标缓冲太短：需要 {rect.Count}，实际 {dst.Length}", nameof(dst));

        var heights = new float[rect.Count];
        FillHeightCm(rect, heights);

        for (int i = 0; i < rect.Count; i++)
            dst[i] = (int)MathF.Round(heights[i]);
    }

    private void FillHeightTile(GridRect rect, Tile3 tile, Span<float> dst, TileScratch scratch)
    {
        int worldX0 = rect.MinX + tile.X;
        int worldZ0 = rect.MinZ + tile.Z;
        int width = tile.SizeX;
        int depth = tile.SizeZ;
        int worldXEnd = worldX0 + width - 1;
        int worldZEnd = worldZ0 + depth - 1;

        scratch.Width = width;

        // 1) 每倍频：本 tile 覆盖的 lattice 角值网格（每个角点只哈希一次），同时预计算每列的格号与 u
        for (int o = 0; o < OctaveCount; o++)
        {
            int cell = _cells[o];
            int cxLo = FloorDiv(worldX0, cell);
            int cxHi = FloorDiv(worldXEnd, cell);
            int czLo = FloorDiv(worldZ0, cell);
            int czHi = FloorDiv(worldZEnd, cell);

            int nx = cxHi - cxLo + 2;
            int nz = czHi - czLo + 2;

            scratch.Nx[o] = nx;
            scratch.Cz0[o] = czLo;

            float[] corners = scratch.EnsureCorners(o, nx * nz);
            int index = 0;
            for (int iz = 0; iz < nz; iz++)
            {
                int cz = czLo + iz;
                for (int ix = 0; ix < nx; ix++)
                    corners[index++] = CornerValue(cell, cxLo + ix, cz);
            }

            int rowBase = o * width;
            for (int xx = 0; xx < width; xx++)
            {
                int wx = worldX0 + xx;
                int cx = FloorDiv(wx, cell);
                scratch.Cx[rowBase + xx] = cx - cxLo;
                scratch.U[rowBase + xx] = (wx - cx * cell) / (float)cell;
            }
        }

        // 2) 逐行逐点：v 每行一次；点值保持 0.62*a + 0.28*b + 0.10*c 的左结合顺序
        for (int zz = 0; zz < depth; zz++)
        {
            int worldZ = worldZ0 + zz;

            for (int o = 0; o < OctaveCount; o++)
            {
                int cell = _cells[o];
                int cz = FloorDiv(worldZ, cell);
                scratch.CzRow[o] = cz - scratch.Cz0[o];
                scratch.VRow[o] = (worldZ - cz * cell) / (float)cell;
            }

            int rowStart = rect.Offset(worldX0, worldZ);

            for (int xx = 0; xx < width; xx++)
            {
                float noise = 0.62f * OctaveValue(scratch, 0, xx, width) +
                              0.28f * OctaveValue(scratch, 1, xx, width) +
                              0.10f * OctaveValue(scratch, 2, xx, width);

                dst[rowStart + xx] = HeightCmFromNoise(noise, worldX0 + xx, worldZ);
            }
        }
    }

    private static float OctaveValue(TileScratch scratch, int octave, int xx, int width)
    {
        int nx = scratch.Nx[octave];
        int cellIndex = scratch.Cx[octave * width + xx];
        int baseIndex = cellIndex + nx * scratch.CzRow[octave];

        var corners = scratch.Corners[octave];
        float h00 = corners[baseIndex];
        float h10 = corners[baseIndex + 1];
        float h01 = corners[baseIndex + nx];
        float h11 = corners[baseIndex + nx + 1];

        float u = scratch.U[octave * width + xx];
        float v = scratch.VRow[octave];

        // 与 TerrainNoise.ValueNoise 逐运算同序
        float a = h00 + (h10 - h00) * u;
        float b = h01 + (h11 - h01) * u;
        return a + (b - a) * v;
    }

    private float HeightCmFromNoise(float noise, int x, int z)
    {
        if (_biomes is null)
            return _constants.HeightOffsetCm + noise * _constants.HeightScaleCm;

        // 与 TerrainHeightModel.HeightCm 同一条公式（同一个 Evaluate，含左结合顺序）
        return HeightFormula.Evaluate(_constants, _biomes, _model.SampleBiome(x, z), noise);
    }

    private float CornerValue(int cell, int cx, int cz)
    {
        var key = new TileCache.Key(_signature, cx, cell, cz);
        if (_lattice.TryGet(key, out float cached))
            return cached;

        // 与 TerrainNoise.HashToUnit 同式：单位噪声 → 有符号
        float value = NoisePrimitives.HashToFloat(NoisePrimitives.Hash2D(cx, cz, _seed)) * 2f - 1f;
        _lattice.Put(key, value);
        return value;
    }

    /// <summary>与 <c>TerrainNoise.FloorDiv</c> 逐字一致（不改成整数除法）。</summary>
    private static int FloorDiv(int value, int divisor) =>
        (int)MathF.Floor(value / (float)divisor);

    private Plan CompilePlan()
    {
        var graph = new OperatorGraph();

        var octaveKeys = new TensorKey[OctaveCount];
        int[] octaveIds = new int[OctaveCount];

        for (int o = 0; o < OctaveCount; o++)
        {
            var output = new TensorKey($"terrain_height.octave{o}");
            octaveKeys[o] = output;

            var spec = new OperatorSpec(
                "value_noise2d",
                Array.Empty<TensorKey>(),
                output,
                FieldShape.LatticeCell,
                TilePolicy.Column32,
                CostClass.Hash,
                OctaveSignature(o));

            octaveIds[o] = graph.Add(spec);
        }

        var noiseKey = new TensorKey("terrain_height.noise");
        int noiseId = graph.Add(
            new OperatorSpec(
                "height_octaves",
                octaveKeys,
                noiseKey,
                FieldShape.Column2D,
                TilePolicy.Column32,
                CostClass.Flop),
            octaveIds);

        if (_biomes is null)
        {
            var root = new TensorKey("terrain_height.cm");
            var linear = new OperatorSpec(
                "height_linear",
                new[] { noiseKey },
                root,
                FieldShape.Column2D,
                TilePolicy.Column32,
                CostClass.Flop,
                ConstantsSignature());

            graph.Add(linear, new[] { noiseId });
            return new PlanCompiler().Compile(graph, root);
        }

        // 气候 + 群系选择：同一列上的连续判定（W8 的「每列一次」在这里体现，融合为一个节点，不物化）
        var biomeKey = new TensorKey("terrain_height.biome");
        var biomeSpec = new OperatorSpec(
            "climate_biome_pick",
            Array.Empty<TensorKey>(),
            biomeKey,
            FieldShape.Column2D,
            TilePolicy.Column32,
            CostClass.Band,
            _signature);

        int biomeId = graph.Add(biomeSpec);

        var heightKey = new TensorKey("terrain_height.cm");
        var formulaSpec = new OperatorSpec(
            "height_formula",
            new[] { noiseKey, biomeKey },
            heightKey,
            FieldShape.Column2D,
            TilePolicy.Column32,
            CostClass.Flop,
            ConstantsSignature());

        graph.Add(formulaSpec, new[] { noiseId, biomeId });
        return new PlanCompiler().Compile(graph, heightKey);
    }

    private long OctaveSignature(int octave)
    {
        long hash = SignatureSet.Mix(SignatureSet.Seed, _seed);
        return SignatureSet.Mix(hash, _cells[octave]);
    }

    private long ConstantsSignature()
    {
        long hash = SignatureSet.Mix(SignatureSet.Seed, _constants.HeightOffsetCm);
        hash = SignatureSet.Mix(hash, _constants.HeightScaleCm);
        return SignatureSet.Mix(hash, _unitsPerMeter);
    }

    /// <summary>每次 fill 的局部 scratch（不共享 ⇒ 可并发；P4 换成 Arena 复用）。</summary>
    private sealed class TileScratch
    {
        public TileScratch(int tileSize)
        {
            Width = tileSize;
            Cx = new int[OctaveCount * tileSize];
            U = new float[OctaveCount * tileSize];
            Nx = new int[OctaveCount];
            Cz0 = new int[OctaveCount];
            CzRow = new int[OctaveCount];
            VRow = new float[OctaveCount];
            Corners = new[] { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>() };
        }

        public int Width;
        public readonly int[] Cx;
        public readonly float[] U;
        public readonly int[] Nx;
        public readonly int[] Cz0;
        public readonly int[] CzRow;
        public readonly float[] VRow;
        public readonly float[][] Corners;

        public float[] EnsureCorners(int octave, int count)
        {
            if (Corners[octave].Length < count)
                Corners[octave] = new float[count];
            return Corners[octave];
        }
    }
}
