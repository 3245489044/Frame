
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogDiagnosticDebugTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogCategoryRegistry.Clear();
        }


        [TestMethod]
        public void Debug_Register_DuplicateUpdatesDescription()
        {
            LogCategoryRegistry.Clear();
            LogCategoryRegistry.Register("WdUI/Layout", "原始描述");
            LogCategoryRegistry.Register("WdUI/Layout", "更新后的描述");

            var node = LogCategoryRegistry.GetNode("WdUI/Layout");
            Assert.IsNotNull(node, "节点不应为 null");
            Assert.AreEqual("更新后的描述", node.Description,
                $"Description 属性值 = '{node.Description}'");
        }


        [TestMethod]
        public void Debug_RegisterRange_Count()
        {
            LogCategoryRegistry.Clear();
            LogCategoryRegistry.RegisterRange(
                ("Module/Cat1", "分类1"),
                ("Module/Cat2", "分类2"),
                ("Module/Cat3", "分类3")
            );

            var count = LogCategoryRegistry.Count;
            var allPaths = string.Join(", ", LogCategoryRegistry.GetAllPaths());
            Assert.AreEqual(4, count,
                $"Count = {count}, 所有路径: [{allPaths}]");
        }


        [TestMethod]
        public void Debug_InitializeDefaults_Count()
        {
            var assemblyPath = typeof(Common.Diagnostics.LogCategoryLoader).Assembly.Location;
            var dir = System.IO.Path.GetDirectoryName(assemblyPath);
            var configPath = System.IO.Path.Combine(dir!, "log-categories.json");
            var exists = System.IO.File.Exists(configPath);
            System.Diagnostics.Debug.WriteLine($"[DIAG] Assembly.Location = {assemblyPath}");
            System.Diagnostics.Debug.WriteLine($"[DIAG] Config path = {configPath}");
            System.Diagnostics.Debug.WriteLine($"[DIAG] File.Exists = {exists}");

            var testDir = dir;
            while (testDir != null)
            {
                var shareConfigPath = System.IO.Path.Combine(testDir, "Share", "Diagnostics", "log-categories.json");
                if (System.IO.File.Exists(shareConfigPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAG] Found via backtracking: {shareConfigPath}");
                    break;
                }
                var parent = System.IO.Path.GetDirectoryName(testDir);
                if (parent == testDir) break;
                testDir = parent;
            }

            LogCategoryRegistry.Clear();
            LogCategoryRegistry.InitializeDefaults();

            var count = LogCategoryRegistry.Count;
            var allPaths = string.Join(", ", LogCategoryRegistry.GetAllPaths());
            Assert.AreEqual(26, count,
                $"Count = {count}, 所有路径: [{allPaths}]");
        }


        [TestMethod]
        public void Debug_ResolveCombined_Semicolon()
        {
            LogCategoryRegistry.Clear();
            LogCategoryRegistry.Register("A/Leaf1", "L1");
            LogCategoryRegistry.Register("B/Leaf2", "L2");

            var flag1 = LogCategoryRegistry.Resolve("A/Leaf1");
            var flag2 = LogCategoryRegistry.Resolve("B/Leaf2");
            var combined = LogCategoryRegistry.ResolveCombined("A/Leaf1;B/Leaf2");

            Assert.AreNotEqual((ulong)LogCategory.None, (ulong)flag1, "flag1 不应为 0");
            Assert.AreNotEqual((ulong)LogCategory.None, (ulong)flag2, "flag2 不应为 0");
            Assert.AreEqual((ulong)flag1 | (ulong)flag2, combined,
                $"flag1={flag1}, flag2={flag2}, combined={combined}, expected={(ulong)flag1 | (ulong)flag2}");
        }


        [TestMethod]
        public void Debug_CategoryOption_ToDisplayText_Leaf()
        {
            var option = new CategoryOption
            {
                Index = 1,
                Path = "Module/Leaf",
                Description = "描述",
                IsLeaf = true,
                IndentLevel = 1
            };

            var text = option.ToDisplayText();
            Assert.IsFalse(text.Contains("/  ("),
                $"ToDisplayText() = '{text}'");
            Assert.IsTrue(text.Contains("描述"),
                $"ToDisplayText() = '{text}' 应包含 '描述'");
        }
    }
}
