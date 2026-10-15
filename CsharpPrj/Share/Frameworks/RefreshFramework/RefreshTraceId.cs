using System.Threading;

namespace Share.RefreshFramework
{
    /// <summary>
    /// 追踪号契约（L0）：一次刷新一条可检索轨迹，形态 <c>"{source}-{序号:D8}"</c>，进程内单调递增。
    /// <para>零依赖（仅 <see cref="Interlocked"/>），不依赖 Plate；一次刷新贯穿 push→page→dispatch→sink。</para>
    /// </summary>
    public static class RefreshTraceId
    {
        private static long _seq;

        /// <summary>产出单调追踪号：<c>{source}-{序号:D8}</c>（source 空白时回落 "refresh"）。</summary>
        public static string New(string source)
        {
            var name = string.IsNullOrWhiteSpace(source) ? "refresh" : source;
            var n = Interlocked.Increment(ref _seq);
            return $"{name}-{n:D8}";
        }
    }
}
