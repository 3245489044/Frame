using Modules.Regulation;

namespace Operation.StatisticalDriven.Process;

/// <summary>输出字节 → 估算 Token 的启发式（设计文档 5.3；精确 tokenizer 后续可替换）。</summary>
/// <remarks>系数与取整单源：统一委托 Modules.Regulation.ContextEstimator，不在本工程另立 bytes/token 常量。</remarks>
public static class TokenEstimation
{
    /// <summary>估算输出 Token 数：字节 → 字符（<paramref name="bytesPerChar"/>）→ token（ContextEstimator.CharsPerToken）。</summary>
    public static long FromBytes(long outputBytes, double bytesPerChar = ContextEstimator.DefaultBytesPerChar)
        => ContextEstimator.EstimateFromBytes(outputBytes, bytesPerChar).Tokens ?? 0;
}
