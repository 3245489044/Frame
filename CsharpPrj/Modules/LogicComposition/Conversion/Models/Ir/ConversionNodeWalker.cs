
using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>IR 遍历工具（深度优先、确定性顺序）。</summary>
    public static class ConversionNodeWalker
    {
        /// <summary>先根深度优先枚举。</summary>
        public static IEnumerable<ConversionNode> DepthFirst(ConversionNode root)
        {
            if (root is null) yield break;

            yield return root;

            switch (root)
            {
                case ConversionObject obj:
                    foreach (var member in obj.Members)
                    {
                        foreach (var child in DepthFirst(member.Value)) yield return child;
                    }
                    break;

                case ConversionArray array:
                    foreach (var item in array.Items)
                    {
                        foreach (var child in DepthFirst(item)) yield return child;
                    }
                    break;
            }
        }

        /// <summary>节点总数（含根）。</summary>
        public static int Count(ConversionNode root)
        {
            var count = 0;
            foreach (var _ in DepthFirst(root)) count++;
            return count;
        }

        /// <summary>最大深度（根为 1；空/null 为 0）。</summary>
        public static int MaxDepth(ConversionNode root)
        {
            if (root is null) return 0;

            var maxChild = 0;
            switch (root)
            {
                case ConversionObject obj:
                    foreach (var member in obj.Members)
                    {
                        var depth = MaxDepth(member.Value);
                        if (depth > maxChild) maxChild = depth;
                    }
                    break;

                case ConversionArray array:
                    foreach (var item in array.Items)
                    {
                        var depth = MaxDepth(item);
                        if (depth > maxChild) maxChild = depth;
                    }
                    break;
            }

            return maxChild + 1;
        }

        /// <summary>查找首个满足谓词的节点（先根序）。</summary>
        public static ConversionNode? Find(ConversionNode root, Func<ConversionNode, bool> predicate)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));

            foreach (var node in DepthFirst(root))
            {
                if (predicate(node)) return node;
            }
            return null;
        }
    }
}
