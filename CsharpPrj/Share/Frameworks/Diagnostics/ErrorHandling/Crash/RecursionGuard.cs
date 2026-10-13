using System;

namespace Common.Diagnostics
{
    /// <summary>递归深度超限异常（可捕获）——取代进程级不可捕获的 StackOverflow。</summary>
    public sealed class RecursionDepthExceededException : Exception
    {
        public RecursionDepthExceededException(string site, int depth)
            : base($"递归深度超限（site={site}, depth={depth}）：疑似自触发环，已转为受控异常（防进程级栈溢出）。")
        {
            Site = site;
            Depth = depth;
        }

        public string Site { get; }

        public int Depth { get; }
    }

    /// <summary>
    /// 递归热点深度哨兵：把不可捕获的 StackOverflow 降级为可捕获异常。
    /// 线程内计数、O(1) 引用操作，健康路径零日志零分配。
    /// </summary>
    public static class RecursionGuard
    {
        [ThreadStatic]
        private static int _depth;

        public static int Depth => _depth;

        public static IDisposable Enter(string site, int maxDepth = 64)
        {
            if (++_depth > maxDepth)
            {
                _depth--;
                throw new RecursionDepthExceededException(site, maxDepth);
            }
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_depth > 0) _depth--;
            }
        }
    }
}
