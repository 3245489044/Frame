#nullable enable

using Modules.Maintain.Ports;

namespace Modules.Maintain.Reference
{
    /// <summary>
    /// 引用刷新算法：把全工作区中指向旧键的引用改写为新键。
    /// 作用于 <see cref="IDataWorkspace{TData}"/> 的全部数据项 ×
    /// <see cref="IReferenceMap{TData}"/> 的全部引用字段，与具体数据类型解耦。
    /// </summary>
    public static class ReferenceRemapper<TData>
    {
        /// <summary>
        /// 遍历 <paramref name="map"/> 中的每个 源键→目标键 映射，
        /// 把工作区内所有 <paramref name="refMap"/> 登记字段上等于源键的引用改写为目标键。
        /// </summary>
        /// <returns>被改写的引用次数。</returns>
        public static int Rewrite(
            IDataWorkspace<TData> workspace,
            IReferenceMap<TData> refMap,
            IReadOnlyDictionary<string, string> map)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (refMap == null) throw new ArgumentNullException(nameof(refMap));
            if (map == null || map.Count == 0) return 0;

            int rewritten = 0;
            foreach (var item in workspace.Data)
            {
                if (item == null) continue;
                foreach (var field in refMap.Fields)
                {
                    var current = field.Reader(item);
                    if (current == null) continue;
                    if (map.TryGetValue(current, out var newValue) && newValue != current)
                    {
                        field.Writer(item, newValue);
                        rewritten++;
                    }
                }
            }
            return rewritten;
        }
    }
}