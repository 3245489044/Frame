#nullable enable

using Modules.Maintain.Ports;

namespace Modules.Maintain.Operations
{
    /// <summary>
    /// 共享片段合成：从各数据项声明的段中求「共享段」——**每个来源项都声明且值一致**的键，
    /// 再与既有片段段合并（既有顺序优先、来源新键追加；同名以来源为准，数据是唯一真源）。
    /// <para>
    /// 幂等性：迁移后来源项不再内联共享段（已改为引用形态），此时共享集为空、结果恒等于既有片段段，
    /// 故重复执行不产生任何变化。
    /// </para>
    /// </summary>
    internal static class FragmentComposer
    {
        /// <summary>合成共享段。</summary>
        public static IReadOnlyList<FragmentSegment> Compose(
            IReadOnlyList<IReadOnlyList<FragmentSegment>> sources,
            IReadOnlyList<FragmentSegment> existing)
        {
            var declaredIn = new Dictionary<string, int>(StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var conflicted = new HashSet<string>(StringComparer.Ordinal);
            var declaredOrder = new List<string>();

            foreach (var source in sources)
            {
                foreach (var segment in source)
                {
                    if (values.TryGetValue(segment.Key, out var known))
                    {
                        if (!string.Equals(known, segment.Content, StringComparison.Ordinal))
                            conflicted.Add(segment.Key);
                    }
                    else
                    {
                        values[segment.Key] = segment.Content;
                        declaredOrder.Add(segment.Key);
                    }

                    declaredIn[segment.Key] = declaredIn.TryGetValue(segment.Key, out var count)
                        ? count + 1
                        : 1;
                }
            }

            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in declaredOrder)
            {
                if (conflicted.Contains(key)) continue;
                if (declaredIn[key] != sources.Count) continue;
                shared[key] = values[key];
            }

            var result = new List<FragmentSegment>();
            var emitted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var segment in existing)
            {
                if (!emitted.Add(segment.Key)) continue;
                result.Add(shared.TryGetValue(segment.Key, out var value)
                    ? new FragmentSegment(segment.Key, value)
                    : segment);
            }

            foreach (var key in declaredOrder)
            {
                if (!shared.ContainsKey(key)) continue;
                if (!emitted.Add(key)) continue;
                result.Add(new FragmentSegment(key, shared[key]));
            }

            return result;
        }
    }
}
