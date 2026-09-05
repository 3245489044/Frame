
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogCategoryRegistryIntegrationTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogCategoryRegistry.Clear();
        }

        [TestMethod]
        public void Test_Registry_RegisterAndResolve()
        {
            var result = RunTestGroup("LogCategoryRegistry 注册与解析", builder =>
            {
                builder.Add("注册后 Resolve 返回非零位标志", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("WdUI/Layout", "WdUI 布局计算");

                    var flag = LogCategoryRegistry.Resolve("WdUI/Layout");
                    return flag != 0;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_TreeStructure()
        {
            var result = RunTestGroup("LogCategoryRegistry 树形结构", builder =>
            {
                builder.Add("注册后 ListDirectories 返回模块列表", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("WdUI/Layout", "Layout");
                    LogCategoryRegistry.Register("WdUI/Render", "Render");
                    LogCategoryRegistry.Register("FileDriven/Command", "Command");

                    var dirs = LogCategoryRegistry.ListDirectories("/logs").ToList();
                    return dirs.Any(d => d.StartsWith("WdUI"))
                        && dirs.Any(d => d.StartsWith("FileDriven"));
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_FlagUniqueness()
        {
            var result = RunTestGroup("LogCategoryRegistry 位标志唯一性", builder =>
            {
                builder.Add("每个叶子节点分配不同的位标志", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("ModuleA/Cat1", "Category 1");
                    LogCategoryRegistry.Register("ModuleA/Cat2", "Category 2");
                    LogCategoryRegistry.Register("ModuleB/Cat3", "Category 3");

                    var flag1 = LogCategoryRegistry.Resolve("ModuleA/Cat1");
                    var flag2 = LogCategoryRegistry.Resolve("ModuleA/Cat2");
                    var flag3 = LogCategoryRegistry.Resolve("ModuleB/Cat3");

                    return flag1 != flag2
                        && flag1 != flag3
                        && flag2 != flag3
                        && flag1 != 0
                        && flag2 != 0
                        && flag3 != 0;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_CombinedPath()
        {
            var result = RunTestGroup("LogCategoryRegistry 组合路径解析", builder =>
            {
                builder.Add("ResolveCombined 返回组合位标志", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("WdUI/Layout", "Layout");
                    LogCategoryRegistry.Register("FileDriven/Command", "Command");

                    var combined = LogCategoryRegistry.ResolveCombined("WdUI/Layout,FileDriven/Command");
                    var single1 = LogCategoryRegistry.Resolve("WdUI/Layout");
                    var single2 = LogCategoryRegistry.Resolve("FileDriven/Command");

                    return (combined & (ulong)single1) == (ulong)single1
                        && (combined & (ulong)single2) == (ulong)single2;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_ReverseLookup()
        {
            var result = RunTestGroup("LogCategoryRegistry 反向查找", builder =>
            {
                builder.Add("GetPath 返回注册时的路径", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("WdUI/Layout", "Layout");

                    var flag = LogCategoryRegistry.Resolve("WdUI/Layout");
                    var path = LogCategoryRegistry.GetPath((ulong)(LogCategory)flag);

                    return path == "WdUI/Layout";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_ClearAndReinitialize()
        {
            var result = RunTestGroup("LogCategoryRegistry 清空与重新注册", builder =>
            {
                builder.Add("Clear 后重新注册正常工作", () =>
                {
                    LogCategoryRegistry.Clear();

                    var dirsAfterClear = LogCategoryRegistry.ListDirectories("/logs").ToList();
                    var cleared = dirsAfterClear.Count == 0;

                    LogCategoryRegistry.Register("WdUI/Layout", "Layout");
                    LogCategoryRegistry.Register("FileDriven/Command", "Command");

                    var dirsAfterReinit = LogCategoryRegistry.ListDirectories("/logs").ToList();
                    var reinitialized = dirsAfterReinit.Any(d => d.StartsWith("WdUI"))
                        && dirsAfterReinit.Any(d => d.StartsWith("FileDriven"));

                    return cleared && reinitialized;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void Test_Registry_ResolveUnknownPath()
        {
            var result = RunTestGroup("LogCategoryRegistry 未知路径解析", builder =>
            {
                builder.Add("解析未注册的路径返回 0", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("WdUI/Layout", "Layout");

                    var flag = LogCategoryRegistry.Resolve("Unknown/Path");
                    return flag == 0;
                });
            });

            result.AssertAllPassed();
        }
    }
}
