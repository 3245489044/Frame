namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 3D 值噪声的 lattice 角值采样器：每个 chunk 预取一次角值，逐点只做同序三线性插值。
/// <para>数值与 <see cref="NoisePrimitives.ValueNoise3D"/> 逐位一致：同一 <c>(world) / (float)cell</c> 表达式、
/// 同一 <c>xf*xf*(3f-2f*xf)</c> 平滑、同一 <c>c000..c111 → x00/x10/x01/x11 → y0/y1 → lerp(w)</c> 次序。</para>
/// <para>线程约定：实例由持有者的 <see cref="System.Threading.ThreadLocal{T}"/> scratch 独占持有，
/// 不跨线程共享；同一实例的下一次 <see cref="Prepare"/> 会覆盖角值（sampler 只对最近一次 Prepare 有效）。</para>
/// </summary>
internal sealed class LatticeNoise3D
{
    private int[] _xOffset = Array.Empty<int>();
    private int[] _yOffset = Array.Empty<int>();
    private int[] _zOffset = Array.Empty<int>();
    private float[] _xU = Array.Empty<float>();
    private float[] _yV = Array.Empty<float>();
    private float[] _zW = Array.Empty<float>();
    private float[] _corner = Array.Empty<float>();

    private int _nx = 1;
    private int _ny = 1;
    private int _nz = 1;

    /// <summary>预取一个 chunk 的角值（线程私有 scratch，容量按需扩容，稳态零分配）。</summary>
    public void Prepare(int originX, int originY, int originZ, int size, int cellCm, int seed)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "chunk 边长必须为正");
        if (cellCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellCm), "噪声波长必须为正");

        int xCellMin = PrepareAxis(originX, size, cellCm, ref _xOffset, ref _xU, out _nx);
        int yCellMin = PrepareAxis(originY, size, cellCm, ref _yOffset, ref _yV, out _ny);
        int zCellMin = PrepareAxis(originZ, size, cellCm, ref _zOffset, ref _zW, out _nz);

        int corners = _nx * _ny * _nz;
        if (_corner.Length < corners)
            _corner = new float[corners];

        int strideY = _nx;
        int strideZ = _nx * _ny;
        for (int iz = 0; iz < _nz; iz++)
        {
            int wz = zCellMin + iz;
            for (int iy = 0; iy < _ny; iy++)
            {
                int wy = yCellMin + iy;
                int row = strideZ * iz + strideY * iy;
                for (int ix = 0; ix < _nx; ix++)
                {
                    _corner[row + ix] = NoisePrimitives.HashToFloat(
                        NoisePrimitives.Hash3D(xCellMin + ix, wy, wz, seed));
                }
            }
        }
    }

    /// <summary>按 chunk 局部坐标取三线性插值结果（与逐点 <see cref="NoisePrimitives.ValueNoise3D"/> 逐位相同）。</summary>
    public float Sample(int localX, int localY, int localZ)
    {
        int xo = _xOffset[localX];
        int yo = _yOffset[localY];
        int zo = _zOffset[localZ];
        float u = _xU[localX];
        float v = _yV[localY];
        float w = _zW[localZ];

        int strideY = _nx;
        int strideZ = _nx * _ny;
        int i000 = xo + strideY * yo + strideZ * zo;

        float c000 = _corner[i000];
        float c100 = _corner[i000 + 1];
        float c010 = _corner[i000 + strideY];
        float c110 = _corner[i000 + strideY + 1];
        float c001 = _corner[i000 + strideZ];
        float c101 = _corner[i000 + strideZ + 1];
        float c011 = _corner[i000 + strideZ + strideY];
        float c111 = _corner[i000 + strideZ + strideY + 1];

        float x00 = c000 + (c100 - c000) * u;
        float x10 = c010 + (c110 - c010) * u;
        float x01 = c001 + (c101 - c001) * u;
        float x11 = c011 + (c111 - c011) * u;

        float y0 = x00 + (x10 - x00) * v;
        float y1 = x01 + (x11 - x01) * v;

        return y0 + (y1 - y0) * w;
    }

    /// <summary>
    /// 单轴预取：返回该轴最小 lattice 格号；<paramref name="count"/> = 覆盖格数 + 1（角点数）。
    /// <para><c>fx</c> 必须保持 <c>(float)(origin+local) / (float)cellCm</c> 原表达式（换写法会有 1 ULP 漂移）。</para>
    /// </summary>
    private static int PrepareAxis(
        int origin, int size, int cellCm, ref int[] offsets, ref float[] smooth, out int count)
    {
        if (offsets.Length < size)
            offsets = new int[Math.Max(size, 8)];
        if (smooth.Length < size)
            smooth = new float[Math.Max(size, 8)];

        int first = 0;
        int last = 0;
        for (int i = 0; i < size; i++)
        {
            float fx = (float)(origin + i) / (float)cellCm;
            int xi = (int)MathF.Floor(fx);
            if (i == 0)
                first = xi;
            last = xi;

            offsets[i] = xi;
            float xf = fx - xi;
            smooth[i] = xf * xf * (3f - 2f * xf);
        }

        for (int i = 0; i < size; i++)
            offsets[i] -= first;

        count = last - first + 2;
        return first;
    }
}
