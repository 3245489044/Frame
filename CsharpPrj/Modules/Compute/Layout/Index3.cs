namespace Modules.Compute.Layout;

/// <summary>
/// 三维索引契约：<b>扁平、x 最快</b>（<c>offset = x + nx*(y + ny*z)</c>），与现有 chunk/padded 槽位一致。
/// <para>禁止 <c>T[,,]</c> 进入热路径；所有跨度为显式字段，热循环直接用 <see cref="Offset"/>。</para>
/// </summary>
public readonly struct Index3 : IEquatable<Index3>
{
    public readonly int Nx;
    public readonly int Ny;
    public readonly int Nz;

    /// <summary>x 跨度（x 最快 ⇒ 恒为 1）。</summary>
    public readonly int Sx;

    /// <summary>y 跨度（= Nx）。</summary>
    public readonly int Sy;

    /// <summary>z 跨度（= Nx*Ny）。</summary>
    public readonly int Sz;

    public Index3(int nx, int ny, int nz, int sx, int sy, int sz)
    {
        if (nx < 0 || ny < 0 || nz < 0)
            throw new ArgumentOutOfRangeException(nameof(nx), "维度不能为负");
        Nx = nx;
        Ny = ny;
        Nz = nz;
        Sx = sx;
        Sy = sy;
        Sz = sz;
    }

    /// <summary>x 最快的三维索引（默认契约）。</summary>
    public static Index3 XFastest(int nx, int ny, int nz)
    {
        if (nx < 0 || ny < 0 || nz < 0)
            throw new ArgumentOutOfRangeException(nameof(nx), "维度不能为负");
        return new Index3(nx, ny, nz, 1, nx, checked(nx * ny));
    }

    public int Length => checked(Nx * Ny * Nz);

    public int Offset(int x, int y, int z) => checked(x * Sx + y * Sy + z * Sz);

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)Nx && (uint)y < (uint)Ny && (uint)z < (uint)Nz;

    public bool Equals(Index3 other) =>
        Nx == other.Nx && Ny == other.Ny && Nz == other.Nz &&
        Sx == other.Sx && Sy == other.Sy && Sz == other.Sz;

    public override bool Equals(object? obj) => obj is Index3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Nx, Ny, Nz, Sx, Sy, Sz);

    public override string ToString() => $"Index3({Nx}x{Ny}x{Nz}; {Sx},{Sy},{Sz})";
}
