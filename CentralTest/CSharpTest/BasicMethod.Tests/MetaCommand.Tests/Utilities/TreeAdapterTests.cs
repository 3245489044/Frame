using MetaCommand;
using MetaCommand.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Utilities
{
    [TestClass]
    public class TreeAdapterTests
    {
        [TestMethod]
        public void TreeNode_DefaultProperties_AreSetCorrectly()
        {
            var node = new TreeNode();
            Assert.IsNotNull(node.Children);
            Assert.AreEqual(0, node.Children.Count);
            Assert.IsNull(node.ParentId);
            Assert.IsTrue(node.IsRoot);
            Assert.IsTrue(node.IsLeaf);
        }

        [TestMethod]
        public void BuildTree_FromList_BuildsCorrectly()
        {
            var items = new List<(string id, string? parentId, string name)>
            {
                ("root", null, "Root"),
                ("child1", "root", "Child1"),
                ("child2", "root", "Child2"),
                ("grandchild", "child1", "GrandChild")
            };
            var tree = TreeAdapter.BuildTree(items, i => i.id, i => i.parentId);
            Assert.IsNotNull(tree);
            Assert.AreEqual(1, tree.Count);
            Assert.AreEqual("root", tree[0].Id);
            Assert.AreEqual(2, tree[0].Children.Count);
            Assert.AreEqual("grandchild", tree[0].Children[0].Children[0].Id);
        }

        [TestMethod]
        public void BuildTree_EmptyList_ReturnsEmptyList()
        {
            var items = new List<(string id, string? parentId)>();
            var tree = TreeAdapter.BuildTree(items, i => i.id, i => i.parentId);
            Assert.AreEqual(0, tree.Count);
        }

        [TestMethod]
        public void BuildTree_MultipleRoots_ReturnsMultipleRoots()
        {
            var items = new List<(string id, string? parentId)>
            {
                ("root1", null),
                ("root2", null)
            };
            var tree = TreeAdapter.BuildTree(items, i => i.id, i => i.parentId);
            Assert.AreEqual(2, tree.Count);
        }

        [TestMethod]
        public void TraverseDepthFirst_ReturnsAllNodesInOrder()
        {
            var root = new TreeNode { Id = "root", Name = "Root" };
            var child1 = new TreeNode { Id = "child1", Name = "Child1" };
            var child2 = new TreeNode { Id = "child2", Name = "Child2" };
            root.Children.Add(child1);
            root.Children.Add(child2);

            var flat = TreeAdapter.TraverseDepthFirst(new[] { root }).ToList();
            Assert.AreEqual(3, flat.Count);
            Assert.AreEqual("root", flat[0].Id);
            Assert.AreEqual("child1", flat[1].Id);
            Assert.AreEqual("child2", flat[2].Id);
        }

        [TestMethod]
        public void TraverseBreadthFirst_ReturnsAllNodesInOrder()
        {
            var root = new TreeNode { Id = "root", Name = "Root" };
            var child1 = new TreeNode { Id = "child1", Name = "Child1" };
            var child2 = new TreeNode { Id = "child2", Name = "Child2" };
            root.Children.Add(child1);
            root.Children.Add(child2);

            var flat = TreeAdapter.TraverseBreadthFirst(new[] { root }).ToList();
            Assert.AreEqual(3, flat.Count);
            Assert.AreEqual("root", flat[0].Id);
        }

        [TestMethod]
        public void FindById_ExistingNode_ReturnsNode()
        {
            var root = new TreeNode { Id = "root" };
            var child = new TreeNode { Id = "target" };
            root.Children.Add(child);

            var found = TreeAdapter.FindById(new[] { root }, "target");
            Assert.IsNotNull(found);
            Assert.AreSame(child, found);
        }

        [TestMethod]
        public void FindById_NonExistentNode_ReturnsNull()
        {
            var root = new TreeNode { Id = "root" };
            var found = TreeAdapter.FindById(new[] { root }, "nonExistent");
            Assert.IsNull(found);
        }

        [TestMethod]
        public void GetLeaves_ReturnsOnlyLeafNodes()
        {
            var root = new TreeNode { Id = "root" };
            var child1 = new TreeNode { Id = "child1" };
            var child2 = new TreeNode { Id = "child2" };
            root.Children.Add(child1);
            root.Children.Add(child2);

            var leaves = TreeAdapter.GetLeaves(new[] { root }).ToList();
            Assert.AreEqual(2, leaves.Count);
            Assert.IsTrue(leaves.All(l => l.IsLeaf));
        }

        [TestMethod]
        public void GetHeight_ReturnsMaxDepth()
        {
            var root = new TreeNode { Id = "root", Depth = 0 };
            var child = new TreeNode { Id = "child", Depth = 1 };
            var grandchild = new TreeNode { Id = "grandchild", Depth = 2 };
            root.Children.Add(child);
            child.Children.Add(grandchild);

            Assert.AreEqual(2, TreeAdapter.GetHeight(new[] { root }));
        }

        [TestMethod]
        public void Flatten_ReturnsAllNodesDepthFirst()
        {
            var root = new TreeNode { Id = "root" };
            var child1 = new TreeNode { Id = "child1" };
            var child2 = new TreeNode { Id = "child2" };
            root.Children.Add(child1);
            root.Children.Add(child2);

            var flat = TreeAdapter.Flatten(new[] { root });
            Assert.AreEqual(3, flat.Count);
        }
    }
}
