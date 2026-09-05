namespace Share.StateManagement
{
    /// <summary>
    /// StateKey 与 IStorageCell 键互转辅助：统一状态键 → cell 键 = FullKey。
    /// </summary>
    public static class StateKeyExtensions
    {
        /// <summary>StateKey → cell 键（Session 层读写用）。</summary>
        public static string ToCellKey(this StateKey key) => key.FullKey;

        /// <summary>cell 键 → StateKey（按 FullKey 三段式反解；无法反解返回 null）。</summary>
        public static StateKey? FromCellKey(string fullKey)
        {
            if (string.IsNullOrWhiteSpace(fullKey))
                return null;

            var parts = fullKey.Split(':', 3);
            return parts.Length == 3
                ? new StateKey(parts[0], parts[1], parts[2])
                : null;
        }
    }
}
