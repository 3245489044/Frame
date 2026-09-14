namespace Operation.StatisticalDriven.Process;

/// <summary>输出字节 → 估算 Token 的启发式（设计文档 5.3；精确 tokenizer 后续可替换）。</summary>
public static class TokenEstimation
{
    /// <summary>估算输出 Token 数：默认按 4 字节/token（英文文本近似）。</summary>
    public static long FromBytes(long outputBytes, int bytesPerToken = 4)
    {
        if (outputBytes <= 0) return 0;
        return Math.Max(1, outputBytes / Math.Max(1, bytesPerToken));
    }
}