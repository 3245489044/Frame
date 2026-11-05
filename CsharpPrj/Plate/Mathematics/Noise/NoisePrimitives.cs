namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 确定性噪声原语（FBM / 值噪声 / 哈希）。
/// <para>自 Engine VoxelMath 提取：只依赖 BCL，浮点运算顺序保持原实现（同 seed 结果逐值一致）。</para>
/// </summary>
public static class NoisePrimitives
{
    /// <summary>
    /// <see cref="Hash3D"/> 调用次数（只增诊断计数，运行时不读）。探针单线程使用，不做线程安全保证。
    /// </summary>
    public static long Hash3DCallCount;

    public static float Fbm(float x, float z, int octaves, int seed)
    {
        float total = 0f, amplitude = 1f, frequency = 1f, max = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += ValueNoise2D(x * frequency, z * frequency, seed + i * 17) * amplitude;
            max += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return total / max;
    }

    public static float ValueNoise2D(float x, float z, int seed)
    {
        int xi = (int)System.MathF.Floor(x);
        int zi = (int)System.MathF.Floor(z);
        float xf = x - xi;
        float zf = z - zi;

        float u = xf * xf * (3f - 2f * xf);
        float v = zf * zf * (3f - 2f * zf);

        float v00 = HashToFloat(Hash2D(xi, zi, seed));
        float v10 = HashToFloat(Hash2D(xi + 1, zi, seed));
        float v01 = HashToFloat(Hash2D(xi, zi + 1, seed));
        float v11 = HashToFloat(Hash2D(xi + 1, zi + 1, seed));

        float a = v00 + (v10 - v00) * u;
        float b = v01 + (v11 - v01) * u;
        return a + (b - a) * v;
    }

    public static float ValueNoise3D(float x, float y, float z, int seed)
    {
        int xi = (int)System.MathF.Floor(x);
        int yi = (int)System.MathF.Floor(y);
        int zi = (int)System.MathF.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;

        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float w = zf * zf * (3f - 2f * zf);

        float c000 = HashToFloat(Hash3D(xi, yi, zi, seed));
        float c100 = HashToFloat(Hash3D(xi + 1, yi, zi, seed));
        float c010 = HashToFloat(Hash3D(xi, yi + 1, zi, seed));
        float c110 = HashToFloat(Hash3D(xi + 1, yi + 1, zi, seed));
        float c001 = HashToFloat(Hash3D(xi, yi, zi + 1, seed));
        float c101 = HashToFloat(Hash3D(xi + 1, yi, zi + 1, seed));
        float c011 = HashToFloat(Hash3D(xi, yi + 1, zi + 1, seed));
        float c111 = HashToFloat(Hash3D(xi + 1, yi + 1, zi + 1, seed));

        float x00 = c000 + (c100 - c000) * u;
        float x10 = c010 + (c110 - c010) * u;
        float x01 = c001 + (c101 - c001) * u;
        float x11 = c011 + (c111 - c011) * u;

        float y0 = x00 + (x10 - x00) * v;
        float y1 = x01 + (x11 - x01) * v;

        return y0 + (y1 - y0) * w;
    }

    public static uint Hash2D(int x, int z, int seed)
    {
        uint h = (uint)((x * 374761393) ^ (z * 668265263) ^ (seed * 2147483647));
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    public static uint Hash3D(int x, int y, int z, int seed)
    {
        Hash3DCallCount++;
        uint h = (uint)((x * 374761393) ^ (y * 668265263) ^ (z * 1274126177) ^ (seed * 2147483647));
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    public static float HashToFloat(uint h) => (h & 0x00FFFFFF) * (1f / 0x01000000);
}
