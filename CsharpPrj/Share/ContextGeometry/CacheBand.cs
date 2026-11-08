/// <summary>
/// 缓存带（B0–B6）：B0/B1 可进 system 前缀；B5 起禁入（追加尾部）。
/// 段序 = Band 升序 + 同带内声明序（禁再排序）。
/// </summary>
public enum CacheBand
{
    B0 = 0,
    B1 = 1,
    B2 = 2,
    B3 = 3,
    B4 = 4,
    B5 = 5,

    /// <summary>B6 逐拍：挂载成员运行态等每次读取即变的段，恒置上下文最末。</summary>
    B6 = 6,
}

/// <summary>缓存带判据（唯一实现）。</summary>
public static class CacheBandRules
{
    /// <summary>是否允许进入冻结前缀（B5 起为尾部追加，禁入）。</summary>
    public static bool IsPrefixAllowed(CacheBand band) => band < CacheBand.B5;

    /// <summary>是否允许进入 LLM system 前缀（仅 B0/B1；B2 起均不满足「版本/能力级稳定」）。</summary>
    public static bool IsSystemPrefix(CacheBand band) => band is CacheBand.B0 or CacheBand.B1;
}