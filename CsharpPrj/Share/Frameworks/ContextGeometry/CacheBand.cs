namespace Share.ContextGeometry;

/// <summary>
/// 缓存带（B0–B5）：B0/B1 可进前缀，B5 禁入（追加尾部）。
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
}

/// <summary>缓存带判据（唯一实现）。</summary>
public static class CacheBandRules
{
    /// <summary>是否允许进入冻结前缀（B5 = 尾部追加，禁入）。</summary>
    public static bool IsPrefixAllowed(CacheBand band) => band != CacheBand.B5;

    /// <summary>文本 → 缓存带（未知/空 ⇒ false，不静默取默认）。</summary>
    public static bool TryParse(string? value, out CacheBand band)
    {
        band = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out band)
               && Enum.IsDefined(typeof(CacheBand), band);
    }
}
