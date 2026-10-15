namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 刷新范围键（L0 契约，零领域 / 零 HTML / 零 Plate）：<see cref="Key"/> 为 null/空白 = 显式全量。
    /// <para>消费方：L0 契约测试（<c>RefreshArbiterTests.L0_Contracts_ShapeAndTraceId</c>）。</para>
    /// </summary>
    public readonly record struct RefreshScope(string? Key)
    {
        /// <summary>是否显式全量（Key 为 null / 空白）。</summary>
        public bool IsAll => string.IsNullOrWhiteSpace(Key);
    }
}
