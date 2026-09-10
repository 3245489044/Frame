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
            bool allowOverwrite = true)
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

        public static List<ControlMetadata> FindByType(IReadOnlyList<ControlMetadata> controls, string typeName)
        {
            ArgumentNullException.ThrowIfNull(controls);
            if (string.IsNullOrEmpty(typeName)) return new List<ControlMetadata>();

            var byName = BuildNameIndex(controls);

            return byName.Values
                .Where(c => string.Equals(c.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static Dictionary<string, ControlMetadata> BuildNameIndex(
            IReadOnlyList<ControlMetadata> controls)
        {
            var dict = new Dictionary<string, ControlMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in controls)
            {
                if (c != null && !string.IsNullOrEmpty(c.Name) && !dict.ContainsKey(c.Name))
                    dict[c.Name] = c;
            }
            return dict;
        }
    }
}
