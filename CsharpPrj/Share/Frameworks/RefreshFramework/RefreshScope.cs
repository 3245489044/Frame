namespace Share.RefreshFramework
{
    /// <summary>
    /// 刷新范围键（L0 契约，零领域 / 零 HTML / 零 Plate）：<see cref="Key"/> 为 null/空白 = 显式全量。
    /// </summary>
    public readonly record struct RefreshScope(string? Key)
    {
        /// <summary>是否显式全量（Key 为 null / 空白）。</summary>
        public bool IsAll => string.IsNullOrWhiteSpace(Key);
    }
}
