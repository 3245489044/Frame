namespace MathematicalAlgorithms.Core;

/// <summary>
/// 稳定内容签名（FNV-1a 64 位）：把「公式参数 + 版本」压成一个可比较的 <see cref="long"/>。
/// <para>用于派生缓存命中判定：签名相等 ⇒ 输入未变，可直接复用；不等 ⇒ 失效重建。
/// 纯整数运算、与进程无关，不依赖 <see cref="System.HashCode"/>（后者带随机种子，不能跨会话比较）。</para>
/// </summary>
public static class SignatureHash
{
    private const long OffsetBasis = 1469598103934665603L;
    private const long Prime = 1099511628211L;

    /// <summary>签名链起点。</summary>
    public static long Begin() => OffsetBasis;

    public static long Mix(long hash, long value) => (hash ^ value) * Prime;

    public static long Mix(long hash, int value) => Mix(hash, (long)value);

    /// <summary>浮点按位参与（同值同签名，NaN 不参与语义）。</summary>
    public static long Mix(long hash, float value) =>
        Mix(hash, (long)BitConverter.SingleToInt32Bits(value));
}
