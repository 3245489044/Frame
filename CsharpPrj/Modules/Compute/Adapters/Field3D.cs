namespace Modules.Compute.Adapters;

/// <summary>
/// 三维标量场适配器：体素级委托接入（<b>边界层，非热路径</b>）。
/// <para>用途：把既有 <c>Apply(x,y,z)</c> 契约原样包一层，供对拍/过渡期使用；热路径应把闭式抬到 tile 上。</para>
/// </summary>
public readonly struct Field3D
{
    public readonly Layout.Index3 Extent;
    private readonly Func<int, int, int, float>? _sampler;

    public Field3D(Layout.Index3 extent, Func<int, int, int, float> sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Extent = extent;
        _sampler = sampler;
    }

    public bool IsValid => _sampler is not null;

    public float Sample(int x, int y, int z)
    {
        if (_sampler is null)
            throw new InvalidOperationException("Field3D 未绑定采样器");
        if (!Extent.InBounds(x, y, z))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y},{z}) 不在 {Extent}");
        return _sampler(x, y, z);
    }

    /// <summary>按 x 最快的扁平顺序抄写整个 extent。</summary>
    public void CopyTo(Span<float> dst)
    {
        if (_sampler is null)
            throw new InvalidOperationException("Field3D 未绑定采样器");
        if (dst.Length < Extent.Length)
            throw new ArgumentException($"目标缓冲太短：需要 {Extent.Length}，实际 {dst.Length}", nameof(dst));

        int index = 0;
        for (int z = 0; z < Extent.Nz; z++)
        {
            for (int y = 0; y < Extent.Ny; y++)
            {
                for (int x = 0; x < Extent.Nx; x++)
                    dst[index++] = _sampler(x, y, z);
            }
        }
    }
}
