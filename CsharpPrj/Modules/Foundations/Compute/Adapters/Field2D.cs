namespace Modules.Compute.Adapters;

/// <summary>
/// 二维标量场适配器：把既有「按列取值」的委托接到 <see cref="GridRect"/> 上。
/// <para><b>边界层，非热路径</b>：委托调用只允许出现在抄写/对照代码；热循环必须先把闭式抬到 tile 上，再走具名核。</para>
/// </summary>
public readonly struct Field2D
{
    public readonly GridRect Rect;
    private readonly Func<int, int, float>? _sampler;

    public Field2D(GridRect rect, Func<int, int, float> sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Rect = rect;
        _sampler = sampler;
    }

    public bool IsValid => _sampler is not null;

    public float Sample(int x, int z)
    {
        if (_sampler is null)
            throw new InvalidOperationException("Field2D 未绑定采样器");
        if (!Rect.Contains(x, z))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{z}) 不在 {Rect}");
        return _sampler(x, z);
    }

    /// <summary>把矩形内全部列抄进 x 最快的一维缓冲（顺序与 <see cref="GridRect.Offset"/> 一致）。</summary>
    public void CopyTo(Span<float> dst)
    {
        if (_sampler is null)
            throw new InvalidOperationException("Field2D 未绑定采样器");
        if (dst.Length < Rect.Count)
            throw new ArgumentException($"目标缓冲太短：需要 {Rect.Count}，实际 {dst.Length}", nameof(dst));

        int w = Rect.Width;
        int index = 0;
        for (int z = Rect.MinZ; z <= Rect.MaxZ; z++)
        {
            for (int x = Rect.MinX; x <= Rect.MaxX; x++)
                dst[index++] = _sampler(x, z);
        }
    }
}
