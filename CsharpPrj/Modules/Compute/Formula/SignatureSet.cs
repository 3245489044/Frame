namespace Modules.Compute.Formula;

/// <summary>
/// 稳定签名（FNV-1a 64bit）：用于 <see cref="TileCache"/> 键与计划签名。
/// <para>只做「混合」不做「加密」；同一组输入必得同一签名，跨进程/跨会话一致
/// （不依赖 <c>object.GetHashCode</code> 的运行期随机化）。</para>
/// </summary>
public static class SignatureSet
{
    /// <summary>FNV-1a 64bit 偏移基。</summary>
    public const long Seed = unchecked((long)14695981039346656037UL);

    private const ulong Prime = 1099511628211UL;

    public static long Mix(long hash, long value)
    {
        hash ^= value;
        hash *= unchecked((long)Prime);
        return hash;
    }

    public static long Mix(long hash, int value) => Mix(hash, (long)value);

    public static long Mix(long hash, uint value) => Mix(hash, (long)value);

    /// <summary>按 IEEE 位模式混合：<c>-0f</c> 与 <c>0f</c> 不同签名（对 A 档是正确行为）。</summary>
    public static long Mix(long hash, float value) => Mix(hash, (long)BitConverter.SingleToInt32Bits(value));

    public static long Mix(long hash, double value) => Mix(hash, BitConverter.DoubleToInt64Bits(value));

    public static long Mix(long hash, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var h = hash;
        foreach (char c in value)
        {
            h ^= c;
            h *= unchecked((long)Prime);
        }

        return h;
    }

    /// <summary>混合一组字符串（顺序敏感）。</summary>
    public static long MixAll(long hash, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        long h = Mix(hash, values.Count);
        foreach (string value in values)
            h = Mix(h, value);
        return h;
    }
}
