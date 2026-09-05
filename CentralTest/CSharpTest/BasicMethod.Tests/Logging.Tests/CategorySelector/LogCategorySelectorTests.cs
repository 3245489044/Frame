
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogCategorySelectorTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogCategoryRegistry.Clear();
            LogRepository.Reset();
        }


        [TestMethod]
        public void Test_Selector_GetSelectableCategories()
        {
            RunTestGroup("LogCategorySelector GetSelectableCategories", builder =>
            {
                builder.Add("返回所有叶子节点", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf1", "Leaf 1");
                    LogCategoryRegistry.Register("Module/Leaf2", "Leaf 2");
                    LogCategoryRegistry.Register("Module/Sub/Leaf3", "Leaf 3");

                    var options = LogCategorySelector.GetSelectableCategories();
                    return options.Count == 3
                        && options.All(o => o.IsLeaf);
                });

                builder.Add("每个选项包含正确的属性", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf", "描述");

                    var options = LogCategorySelector.GetSelectableCategories();
                    var first = options[0];
                    return first.Index == 1
                        && first.Path == "Module/Leaf"
                        && first.Description == "描述"
                        && first.Category != LogCategory.None
                        && first.IsLeaf;
                });

                builder.Add("选项按路径字母排序", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("B/Leaf", "B");
                    LogCategoryRegistry.Register("A/Leaf", "A");

                    var options = LogCategorySelector.GetSelectableCategories();
                    return options[0].Path == "A/Leaf"
                        && options[1].Path == "B/Leaf";
                });

                builder.Add("空注册表返回空列表", () =>
                {
                    LogCategoryRegistry.Clear();
                    var options = LogCategorySelector.GetSelectableCategories();
                    return options.Count == 0;
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_GetTreeCategories()
        {
            RunTestGroup("LogCategorySelector GetTreeCategories", builder =>
            {
                builder.Add("返回所有节点（含中间节点）", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf1", "L1");
                    LogCategoryRegistry.Register("Module/Leaf2", "L2");

                    var options = LogCategorySelector.GetTreeCategories();
                    return options.Count == 3;
                });

                builder.Add("中间节点 IsLeaf=false", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf", "L");

                    var options = LogCategorySelector.GetTreeCategories();
                    var module = options.First(o => o.Path == "Module");
                    return !module.IsLeaf;
                });

                builder.Add("叶子节点 IsLeaf=true", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf", "L");

                    var options = LogCategorySelector.GetTreeCategories();
                    var leaf = options.First(o => o.Path == "Module/Leaf");
                    return leaf.IsLeaf;
                });

                builder.Add("缩进级别正确", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("A/B/C/Leaf", "Deep");

                    var options = LogCategorySelector.GetTreeCategories();
                    var leaf = options.First(o => o.Path == "A/B/C/Leaf");
                    return leaf.IndentLevel == 3;
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_BuildFilter()
        {
            RunTestGroup("LogCategorySelector BuildFilter", builder =>
            {
                builder.Add("根据序号构建过滤器", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Cat1", "Cat1");
                    LogCategoryRegistry.Register("Module/Cat2", "Cat2");
                    LogCategoryRegistry.Register("Module/Cat3", "Cat3");

                    var options = LogCategorySelector.GetSelectableCategories();
                    var cat1 = options.First(o => o.Path == "Module/Cat1");
                    var cat3 = options.First(o => o.Path == "Module/Cat3");

                    var filter = LogCategorySelector.BuildFilter(
                        new List<int> { cat1.Index, cat3.Index });

                    return filter.Path.Contains("Module/Cat1")
                        && filter.Path.Contains("Module/Cat3")
                        && !filter.Path.Contains("Module/Cat2");
                });

                builder.Add("BuildFilter 带级别和关键词参数", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Cat1", "Cat1");

                    var options = LogCategorySelector.GetSelectableCategories();
                    var filter = LogCategorySelector.BuildFilter(
                        new List<int> { options[0].Index },
                        level: "ERROR",
                        since: "1h",
                        limit: 50,
                        keyword: "test");

                    return filter.Level == "ERROR"
                        && filter.Since == "1h"
                        && filter.Limit == 50
                        && filter.Keyword == "test"
                        && filter.Recursive == false;
                });

                builder.Add("空序号列表返回空路径", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Cat1", "Cat1");

                    var filter = LogCategorySelector.BuildFilter(new List<int>());
                    return string.IsNullOrEmpty(filter.Path);
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_BuildFilterFromPaths()
        {
            RunTestGroup("LogCategorySelector BuildFilterFromPaths", builder =>
            {
                builder.Add("根据路径列表构建过滤器", () =>
                {
                    var filter = LogCategorySelector.BuildFilterFromPaths(
                        new List<string> { "Module/Cat1", "Module/Cat2" });

                    return filter.Path == "Module/Cat1,Module/Cat2"
                        && filter.Recursive == false;
                });

                builder.Add("BuildFilterFromPaths 带级别和时间参数", () =>
                {
                    var filter = LogCategorySelector.BuildFilterFromPaths(
                        new List<string> { "Module/Cat1" },
                        level: "WARN",
                        since: "30m",
                        limit: 200);

                    return filter.Level == "WARN"
                        && filter.Since == "30m"
                        && filter.Limit == 200;
                });

                builder.Add("空路径列表返回空字符串", () =>
                {
                    var filter = LogCategorySelector.BuildFilterFromPaths(new List<string>());
                    return filter.Path == string.Empty;
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_GetMenuText()
        {
            RunTestGroup("LogCategorySelector GetMenuText", builder =>
            {
                builder.Add("GetMenuText 返回非空字符串", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Cat1", "Category 1");
                    LogCategoryRegistry.Register("Module/Cat2", "Category 2");

                    var text = LogCategorySelector.GetMenuText();
                    return !string.IsNullOrEmpty(text)
                        && text.Contains("Module")
                        && text.Contains("Cat1")
                        && text.Contains("Cat2");
                });

                builder.Add("GetMenuText 包含操作提示", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Cat1", "Cat1");

                    var text = LogCategorySelector.GetMenuText();
                    return text.Contains("[A]")
                        && text.Contains("[0]")
                        && text.Contains("请输入序号");
                });

                builder.Add("GetMenuText 按模块分组", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("ModuleA/Leaf1", "A1");
                    LogCategoryRegistry.Register("ModuleB/Leaf2", "B2");

                    var text = LogCategorySelector.GetMenuText();
                    return text.Contains("[ModuleA]")
                        && text.Contains("[ModuleB]");
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_GetStatsText()
        {
            RunTestGroup("LogCategorySelector GetStatsText", builder =>
            {
                builder.Add("GetStatsText 返回非空字符串", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf1", "L1");
                    LogCategoryRegistry.Register("Module/Leaf2", "L2");

                    var text = LogCategorySelector.GetStatsText();
                    return !string.IsNullOrEmpty(text)
                        && text.Contains("Module")
                        && text.Contains("Leaf1")
                        && text.Contains("Leaf2");
                });

                builder.Add("GetStatsText 包含总计信息", () =>
                {
                    LogCategoryRegistry.Clear();
                    LogCategoryRegistry.Register("Module/Leaf", "L");

                    var text = LogCategorySelector.GetStatsText();
                    return text.Contains("总计")
                        && text.Contains("2");
                });

                builder.Add("空注册表返回 0 个分类", () =>
                {
                    LogCategoryRegistry.Clear();
                    var text = LogCategorySelector.GetStatsText();
                    return text.Contains("0");
                });
            }).AssertAllPassed();
        }


        [TestMethod]
        public void Test_Selector_CategoryOptionToDisplayText()
        {
            RunTestGroup("LogCategorySelector CategoryOption ToDisplayText", builder =>
            {
                builder.Add("叶子节点显示格式", () =>
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
                    return text.Contains("Module/Leaf")
                        && text.Contains("描述")
                        && !text.EndsWith("/  (");
                });

                builder.Add("中间节点显示格式", () =>
                {
                    var option = new CategoryOption
                    {
                        Index = 1,
                        Path = "Module",
                        Description = "模块",
                        IsLeaf = false,
                        IndentLevel = 0
                    };

                    var text = option.ToDisplayText();
                    return text.Contains("Module/")
                        && text.Contains("模块");
                });

                builder.Add("缩进级别影响显示", () =>
                {
                    var option1 = new CategoryOption { IndentLevel = 0, Path = "Root", Description = "R" };
                    var option2 = new CategoryOption { IndentLevel = 2, Path = "Root/Sub/Leaf", Description = "L" };

                    var text1 = option1.ToDisplayText();
                    var text2 = option2.ToDisplayText();
                    return text2.Length > text1.Length;
                });
            }).AssertAllPassed();
        }
    }
}
