
using Common.Diagnostics;
using SkeletonFlow;
using SkeletonFlow.App;

namespace Logging.Tests
{
    public static class RegistryAssert
    {

        private static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> Assertion(string name, Action<RegistryFixtureContext> assert)
        {
            return new DelegateSkeleton<RegistryFixtureContext, RegistryFixtureContext>($"Assert:{name}",
                (ctx, _) => { assert(ctx); return ctx; });
        }

        private static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> Assertion(string name, Action assert)
        {
            return new DelegateSkeleton<RegistryFixtureContext, RegistryFixtureContext>($"Assert:{name}",
                (ctx, _) => { assert(); return ctx; });
        }


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeExists(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(
                    LogCategoryRegistry.GetNode(path), $"节点 '{path}' 应存在"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeNotExists(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNull(
                    LogCategoryRegistry.GetNode(path), $"节点 '{path}' 应不存在"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> IsLeaf(string name, string path) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(node!.IsLeaf, $"节点 '{path}' 应为叶子节点");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> IsNotLeaf(string name, string path) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsFalse(node!.IsLeaf, $"节点 '{path}' 不应为叶子节点");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeNameEquals(string name, string path, string expected) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, node!.Name);
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeFullPathEquals(string name, string path, string expected) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, node!.FullPath);
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeDescriptionEquals(string name, string path, string expected) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, node!.Description);
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> ChildrenCount(string name, string path, int expected) =>
            Assertion(name, () =>
            {
                var node = LogCategoryRegistry.GetNode(path);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(node, $"节点 '{path}' 应存在");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, node!.Children.Count);
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> BitFlagNonZero(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreNotEqual((ulong)LogCategory.None,
                    (ulong)LogCategoryRegistry.Resolve(path), $"'{path}' 的位标志应非零"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> BitFlagZero(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual((ulong)LogCategory.None,
                    (ulong)LogCategoryRegistry.Resolve(path), $"'{path}' 的位标志应为 0"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> BitFlagEquals(string name, string path1, string path2) =>
            Assertion(name, () =>
            {
                var flag1 = LogCategoryRegistry.Resolve(path1);
                var flag2 = LogCategoryRegistry.Resolve(path2);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreNotEqual((ulong)LogCategory.None, (ulong)flag1, $"'{path1}' 的位标志应非零");
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual((ulong)flag1, (ulong)flag2, $"'{path1}' 和 '{path2}' 的位标志应相等");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> BitFlagValue(string name, string path, LogCategory expected) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual((ulong)expected,
                    (ulong)LogCategoryRegistry.Resolve(path), $"'{path}' 的位标志应为 {expected}"));


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> IsRegistered(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    LogCategoryRegistry.IsRegistered(path), $"'{path}' 应已注册"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> IsNotRegistered(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsFalse(
                    LogCategoryRegistry.IsRegistered(path), $"'{path}' 应未注册"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> CountEquals(string name, int expected) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected,
                    LogCategoryRegistry.Count, $"Count 应为 {expected}"));


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> PathsContain(string name, string path) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    LogCategoryRegistry.GetAllPaths().Contains(path), $"路径列表应包含 '{path}'"));

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> PathsSorted(string name, params string[] expectedOrder) =>
            Assertion(name, () =>
            {
                var paths = LogCategoryRegistry.GetAllPaths().ToList();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedOrder.Length, paths.Count, "路径数量不匹配");
                for (int i = 0; i < expectedOrder.Length; i++)
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedOrder[i], paths[i], $"路径顺序不匹配，索引 {i}");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> TreeRootsCount(string name, int expected) =>
            Assertion(name, () =>
            {
                var tree = LogCategoryRegistry.GetTree();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, tree.Roots.Count, "Roots 数量不匹配");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> TreeFlatMapContains(string name, string path) =>
            Assertion(name, () =>
            {
                var tree = LogCategoryRegistry.GetTree();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    tree.FlatMap.ContainsKey(path), $"FlatMap 应包含 '{path}'");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> TreeJsonContains(string name, string expected) =>
            Assertion(name, () =>
            {
                var json = LogCategoryRegistry.GetTreeJson();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(json);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    json.Contains(expected), $"JSON 应包含 '{expected}'");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> TreeStringContains(string name, string expected) =>
            Assertion(name, () =>
            {
                var tree = LogCategoryRegistry.GetTree();
                var text = tree.ToTreeString();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    text.Contains(expected), $"ToTreeString 应包含 '{expected}'");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> DirectoriesContain(string name, string rootPath, string expectedPrefix) =>
            Assertion(name, () =>
            {
                var dirs = LogCategoryRegistry.ListDirectories(rootPath).ToList();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    dirs.Any(d => d.StartsWith(expectedPrefix)),
                    $"目录列表应包含以 '{expectedPrefix}' 开头的项");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> DirectoriesEmpty(string name, string rootPath) =>
            Assertion(name, () =>
            {
                var dirs = LogCategoryRegistry.ListDirectories(rootPath).ToList();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(0, dirs.Count, $"'{rootPath}' 的目录列表应为空");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> LeafNodesCount(string name, int expected) =>
            Assertion(name, () =>
            {
                var leaves = LogCategoryRegistry.GetLeafNodes().ToList();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, leaves.Count, "叶子节点数量不匹配");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> AllLeavesAreLeaf(string name) =>
            Assertion(name, () =>
            {
                var leaves = LogCategoryRegistry.GetLeafNodes().ToList();
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(
                    leaves.All(l => l.IsLeaf), "所有叶子节点的 IsLeaf 应为 true");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> CombinedContains(string name, string combinedPath, params string[] paths) =>
            Assertion(name, () =>
            {
                var combined = LogCategoryRegistry.ResolveCombined(combinedPath);
                foreach (var p in paths)
                {
                    var flag = LogCategoryRegistry.Resolve(p);
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreNotEqual((ulong)LogCategory.None, (ulong)flag, $"'{p}' 的位标志应非零");
                    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual((ulong)flag, combined & (ulong)flag, $"组合标志应包含 '{p}' 的位标志");
                }
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> CombinedZero(string name, string combinedPath) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(0UL,
                    LogCategoryRegistry.ResolveCombined(combinedPath), $"'{combinedPath}' 的组合标志应为 0"));


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> RegisterThrows(string name, string path, string description) =>
            Assertion(name, () =>
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.ThrowsException<ArgumentException>(() =>
                    LogCategoryRegistry.Register(path, description)));


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> SameNodeReference(string name, string path, string desc1, string desc2) =>
            Assertion(name, () =>
            {
                var node1 = LogCategoryRegistry.Register(path, desc1);
                var node2 = LogCategoryRegistry.Register(path, desc2);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreSame(node1, node2, "重复注册应返回相同节点引用");
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> BitFlagUnchangedOnReRegister(string name, string path, string desc1, string desc2) =>
            Assertion(name, () =>
            {
                var node1 = LogCategoryRegistry.Register(path, desc1);
                var flag1 = node1.Category;
                var node2 = LogCategoryRegistry.Register(path, desc2);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(flag1, node2.Category, "重复注册后位标志应不变");
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeToStringEqualsFullPath(string name, string path, string description) =>
            Assertion(name, () =>
            {
                var node = new CategoryNode("Test", path, description, LogCategory.System);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(path, node.ToString());
            });

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> NodeCategoryReadWrite(string name, string path, string description) =>
            Assertion(name, () =>
            {
                var node = new CategoryNode("Test", path, description, LogCategory.System);
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(LogCategory.System, node.Category);
            });


        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> Do(string name, Action action) =>
            Assertion(name, action);

        public static ISkeleton<RegistryFixtureContext, RegistryFixtureContext> Do(string name, Action<RegistryFixtureContext> action) =>
            Assertion(name, action);
    }
}
