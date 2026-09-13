// netstandard2.0 缺少 Enumerable.ToHashSet（netstandard2.1+/net5+ 才有）。
// 此处按 BCL 同语义补齐（默认比较器 / 指定比较器）。
#if NETSTANDARD2_0

using System.Collections.Generic;

namespace System.Linq
{
    internal static class EnumerableCompatibilityExtensions
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
            => new HashSet<T>(source);

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer)
            => new HashSet<T>(source, comparer);
    }
}

#endif
