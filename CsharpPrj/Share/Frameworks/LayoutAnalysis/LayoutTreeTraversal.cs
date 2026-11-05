#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Framework.Tree;
using Modules.LayoutAnalysis.UIInspector;

namespace Modules.LayoutAnalysis
{
    public static class LayoutTreeTraversal
    {
        public static TreeWalker<ControlMetadata> CreateWalker(IReadOnlyList<ControlMetadata> controls)
        {
            ArgumentNullException.ThrowIfNull(controls);

            return new TreeWalker<ControlMetadata>(c =>
            {
                if (c == null || string.IsNullOrEmpty(c.Name))
                    return Enumerable.Empty<ControlMetadata>();

                return controls.Where(x =>
                    x != null &&
                    x != c &&
                    string.Equals(x.ParentName, c.Name, StringComparison.OrdinalIgnoreCase));
            });
        }

        public static TreePathFinder<ControlMetadata> CreatePathFinder(IReadOnlyList<ControlMetadata> controls)
        {
            ArgumentNullException.ThrowIfNull(controls);

            var byName = BuildNameIndex(controls);

            return new TreePathFinder<ControlMetadata>(c =>
            {
                if (c == null || string.IsNullOrEmpty(c.ParentName))
                    return null;

                return byName.TryGetValue(c.ParentName, out var parent) ? parent : null;
            });
        }

        public static TreeIndex<ControlMetadata, string> CreateIndex(IReadOnlyList<ControlMetadata> controls,
            bool allowOverwrite = false)
        {
            ArgumentNullException.ThrowIfNull(controls);

            var index = new TreeIndex<ControlMetadata, string>(
                StringComparer.OrdinalIgnoreCase, allowOverwrite);

            foreach (var c in controls)
            {
                if (c != null && !string.IsNullOrEmpty(c.Name))
                    index.RegisterNode(c, c.Name);
            }

            return index;
        }

        /// <summary>根集合（含未知父的孤儿；需区分请用 <see cref="GetRootsAndOrphans"/>）。</summary>
        public static List<ControlMetadata> GetRoots(IReadOnlyList<ControlMetadata> controls)
        {
            ArgumentNullException.ThrowIfNull(controls);

            var byName = BuildNameIndex(controls);

            return controls
                .Where(c => c != null &&
                            (string.IsNullOrEmpty(c.ParentName) ||
                             !byName.ContainsKey(c.ParentName)))
                .ToList();
        }

        /// <summary>
        /// 根/孤儿分离（唯一可判定口径）：根 = 无父；孤儿 = 父名存在但索引中查无此父。
        /// <para>「未知父当根」会掩盖断链，故提供分离出口供调用方显式处置（不再只能当根）。</para>
        /// </summary>
        /// <param name="controls">控件集。</param>
        /// <param name="orphans">孤儿（父名非空且父不存在）。</param>
        public static List<ControlMetadata> GetRootsAndOrphans(
            IReadOnlyList<ControlMetadata> controls, out List<ControlMetadata> orphans)
        {
            ArgumentNullException.ThrowIfNull(controls);

            var byName = BuildNameIndex(controls);
            var roots = new List<ControlMetadata>();
            orphans = new List<ControlMetadata>();

            foreach (var c in controls)
            {
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.ParentName))
                {
                    roots.Add(c);
                    continue;
                }

                if (!byName.ContainsKey(c.ParentName)) orphans.Add(c);
            }

            return roots;
        }

        public static List<ControlMetadata> FindByType(IReadOnlyList<ControlMetadata> controls, string typeName)
        {
            ArgumentNullException.ThrowIfNull(controls);
            if (string.IsNullOrEmpty(typeName)) return new List<ControlMetadata>();

            var byName = BuildNameIndex(controls);

            return byName.Values
                .Where(c => string.Equals(c.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>名称索引（同名策略单源：缺省保留先序；<paramref name="allowOverwrite"/>=true 才后序覆盖）。</summary>
        /// <param name="controls">控件集。</param>
        /// <param name="allowOverwrite">同名是否后者覆盖（缺省 false，与 <see cref="CreateIndex"/> 对齐）。</param>
        public static Dictionary<string, ControlMetadata> BuildNameIndex(
            IReadOnlyList<ControlMetadata> controls, bool allowOverwrite = false)
        {
            var dict = new Dictionary<string, ControlMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in controls)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                if (!allowOverwrite && dict.ContainsKey(c.Name)) continue;
                dict[c.Name] = c;
            }
            return dict;
        }
    }
}
