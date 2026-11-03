namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块光环快照装配：把 chunkSize³ 中心载荷放进 (chunkSize+2)³ 容器内侧，外圈逐点采样。
/// <para>纯函数：不认载荷类型与领域版本，只对 <typeparamref name="T"/> 和"取某世界格"的委托成立。
/// 目标索引保持行主序 <c>x + dim*(y + dim*z)</c>，中心索引保持
/// <c>(x-1) + size*((y-1) + size*(z-1))</c>，与网格化/光照/GPU 稠密槽位同式。</para>
/// <para>外圈采样次数 = (size+2)³ - size³（32³ 时约 7.9k 次），调用方负责控制该开销。</para>
/// </summary>
public static class ChunkPadding
{
    /// <summary>构造 chunkSize+2 光环快照。中心区从 <paramref name="center"/> 复制，外圈调用 <paramref name="sample"/>（世界坐标）。</summary>
    public static T[] Pad<T>(
        in ChunkCoord chunk, int chunkSize, ReadOnlySpan<T> center, Func<int, int, int, T> sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        ChunkSpace.ShiftOf(chunkSize); // 2 的幂校验与分块协议一致（非 2 的幂直接抛）

        int volume = chunkSize * chunkSize * chunkSize;
        if (center.Length < volume)
            throw new ArgumentException($"中心载荷不足 chunkSize³：需要 {volume}，实际 {center.Length}", nameof(center));

        int dim = chunkSize + 2;
        var padded = new T[dim * dim * dim];

        int ox = ChunkSpace.ChunkOrigin(chunk.X, chunkSize);
        int oy = ChunkSpace.ChunkOrigin(chunk.Y, chunkSize);
        int oz = ChunkSpace.ChunkOrigin(chunk.Z, chunkSize);

        for (int z = 0; z < dim; z++)
        {
            int wz = oz + z - 1;
            for (int y = 0; y < dim; y++)
            {
                int wy = oy + y - 1;
                int row = dim * (y + dim * z);
                for (int x = 0; x < dim; x++)
                {
                    int dst = row + x;
                    if (x > 0 && x <= chunkSize && y > 0 && y <= chunkSize && z > 0 && z <= chunkSize)
                        padded[dst] = center[(x - 1) + chunkSize * ((y - 1) + chunkSize * (z - 1))];
                    else
                        padded[dst] = sample(ox + x - 1, wy, wz);
                }
            }
        }

        return padded;
    }
}
