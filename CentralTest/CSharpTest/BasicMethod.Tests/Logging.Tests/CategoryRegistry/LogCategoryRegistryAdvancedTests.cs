
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using SkeletonFlow.App;

namespace Logging.Tests
{
    [TestClass]
    public class LogCategoryRegistryAdvancedTests : RegistryTestBase
    {

        [TestMethod]
        public void Test_Registry_Register_NullPath_Throws()
        {
            RunGroup("LogCategoryRegistry Register 空路径参数验证",
                ("null 路径抛出 ArgumentException",
                    ClearThen(RegistryAssert.RegisterThrows("null", null!, "description"))),
                ("空字符串路径抛出 ArgumentException",
                    ClearThen(RegistryAssert.RegisterThrows("empty", "", "description"))),
                ("空白字符串路径抛出 ArgumentException",
                    ClearThen(RegistryAssert.RegisterThrows("whitespace", "   ", "description"))));
        }


        [TestMethod]
        public void Test_Registry_Register_DuplicateUpdatesDescription()
        {
            RunGroup("LogCategoryRegistry 重复注册更新描述",
                ("重复注册同一路径更新描述",
                    RegisterThen("WdUI/Layout", "原始描述",
                        RegistryAssert.NodeDescriptionEquals("描述为原始描述", "WdUI/Layout", "原始描述"),
                        RegistryAssert.Do("ReRegister", () => LogCategoryRegistry.Register("WdUI/Layout", "更新后的描述")),
                        RegistryAssert.NodeDescriptionEquals("描述已更新", "WdUI/Layout", "更新后的描述"))),
                ("重复注册返回相同节点",
                    ClearThen(RegistryAssert.SameNodeReference("相同引用", "WdUI/Layout", "描述1", "描述2"))),
                ("重复注册位标志不变",
                    ClearThen(RegistryAssert.BitFlagUnchangedOnReRegister("位标志不变", "WdUI/Layout", "描述1", "描述2"))));
        }


        [TestMethod]
        public void Test_Registry_Register_AutoCreateIntermediateNodes()
        {
            RunGroup("LogCategoryRegistry 自动创建中间节点",
                ("注册深层路径自动创建中间节点",
                    RegisterThen("Module/SubModule/Leaf", "叶子节点",
                        AssertAllNodesExist("Module", "Module/SubModule", "Module/SubModule/Leaf"),
                        RegistryAssert.ChildrenCount("Module 有 1 个子节点", "Module", 1),
                        RegistryAssert.ChildrenCount("SubModule 有 1 个子节点", "Module/SubModule", 1),
                        RegistryAssert.IsLeaf("Leaf 是叶子节点", "Module/SubModule/Leaf"))),
                ("中间节点位标志为 0",
                    RegisterThen("A/B/C", "叶子",
                        RegistryAssert.BitFlagZero("B 位标志为 0", "A/B"))),
                ("叶子节点位标志非零",
                    RegisterThen("A/B/C", "叶子",
                        RegistryAssert.BitFlagNonZero("C 位标志非零", "A/B/C"))));
        }


        [TestMethod]
        public void Test_Registry_RegisterRange()
        {
            RunGroup("LogCategoryRegistry RegisterRange 批量注册",
                ("RegisterRange 注册多个分类",
                    RegisterRangeThen(
                        new[] { ("Module/Cat1", "分类1"), ("Module/Cat2", "分类2"), ("Module/Cat3", "分类3") },
                        AssertAllRegistered("Module/Cat1", "Module/Cat2", "Module/Cat3"),
                        RegistryAssert.CountEquals("Count=4（含 Module 中间节点）", 4))),
                ("RegisterRange 后 Resolve 返回非零",
                    RegisterRangeThen(
                        new[] { ("Test/Cat1", "Test 1"), ("Test/Cat2", "Test 2") },
                        AssertAllBitFlagsNonZero("Test/Cat1", "Test/Cat2"))));
        }


        [TestMethod]
        public void Test_Registry_RegisterModule()
        {
            RunGroup("LogCategoryRegistry RegisterModule 模块注册",
                ("RegisterModule 注册模块分类",
                    RegisterModuleThen("MyModule",
                        new[] { ("Feature1", "功能1"), ("Feature2", "功能2") },
                        AssertAllRegistered("MyModule/Feature1", "MyModule/Feature2"))),
                ("RegisterModule 自动创建模块根节点",
                    RegisterModuleThen("NewModule",
                        new[] { ("Sub1", "子1") },
                        RegistryAssert.DirectoriesContain("目录包含 NewModule", "/logs", "NewModule"))));
        }


        [TestMethod]
        public void Test_Registry_RegisterTree()
        {
            var treeDef = new Dictionary<string, object>
            {
                ["MyApp"] = new Dictionary<string, object>
                {
                    ["UI"] = new Dictionary<string, object>
                    {
                        ["Render"] = "渲染",
                        ["Layout"] = "布局"
                    },
                    ["Data"] = new Dictionary<string, object>
                    {
                        ["Load"] = "加载",
                        ["Save"] = "保存"
                    }
                }
            };

            RunGroup("LogCategoryRegistry RegisterTree 树形定义注册",
                ("RegisterTree 注册树形分类",
                    RegisterTreeThen(treeDef,
                        AssertAllRegistered(
                            "MyApp/UI/Render", "MyApp/UI/Layout",
                            "MyApp/Data/Load", "MyApp/Data/Save",
                            "MyApp/UI", "MyApp/Data"))),
                ("RegisterTree 叶子节点描述正确",
                    RegisterTreeThen(new Dictionary<string, object>
                    {
                        ["App"] = new Dictionary<string, object>
                        {
                            ["Feature"] = "功能描述"
                        }
                    },
                        RegistryAssert.NodeDescriptionEquals("描述正确", "App/Feature", "功能描述"))));
        }


        [TestMethod]
        public void Test_Registry_GetNode()
        {
            RunGroup("LogCategoryRegistry GetNode",
                ("GetNode 返回已注册的节点",
                    RegisterThen("Module/Leaf", "叶子",
                        RegistryAssert.NodeNameEquals("Name=Leaf", "Module/Leaf", "Leaf"),
                        RegistryAssert.NodeFullPathEquals("FullPath=Module/Leaf", "Module/Leaf", "Module/Leaf"),
                        RegistryAssert.NodeDescriptionEquals("Description=叶子", "Module/Leaf", "叶子"))),
                ("GetNode 不存在的路径返回 null",
                    ClearThen(RegistryAssert.NodeNotExists("不存在返回 null", "NonExistent"))),
                ("GetNode 带虚拟根前缀",
                    RegisterThen("Module/Leaf", "叶子",
                        RegistryAssert.NodeExists("带 /logs 前缀存在", "/logs/Module/Leaf"),
                        RegistryAssert.NodeFullPathEquals("FullPath 无前缀", "/logs/Module/Leaf", "Module/Leaf"))));
        }

        [TestMethod]
        public void Test_Registry_GetLeafNodes()
        {
            RunGroup("LogCategoryRegistry GetLeafNodes",
                ("GetLeafNodes 只返回叶子节点",
                    RegisterRangeThen(
                        new[] { ("Module/Cat1", "Cat1"), ("Module/Cat2", "Cat2"), ("Module/Sub/Cat3", "Cat3") },
                        RegistryAssert.AllLeavesAreLeaf("所有节点都是叶子"),
                        RegistryAssert.LeafNodesCount("3 个叶子节点", 3))),
                ("GetLeafNodes 返回所有叶子节点",
                    RegisterRangeThen(
                        new[] { ("A/X", "X"), ("B/Y", "Y") },
                        RegistryAssert.LeafNodesCount("2 个叶子节点", 2))));
        }

        [TestMethod]
        public void Test_Registry_GetTree()
        {
            RunGroup("LogCategoryRegistry GetTree",
                ("GetTree 返回完整树形结构",
                    RegisterRangeThen(
                        new[] { ("Module/Leaf1", "Leaf1"), ("Module/Leaf2", "Leaf2") },
                        RegistryAssert.TreeRootsCount("1 个根节点", 1),
                        RegistryAssert.ChildrenCount("Module 有 2 个子节点", "Module", 2),
                        AssertAllFlatMapContains("Module", "Module/Leaf1", "Module/Leaf2"))),
                ("GetTree FlatMap 包含所有节点",
                    RegisterThen("A/B/C", "Deep",
                        AssertAllFlatMapContains("A", "A/B", "A/B/C"))));
        }

        [TestMethod]
        public void Test_Registry_GetTreeJson()
        {
            RunGroup("LogCategoryRegistry GetTreeJson",
                ("GetTreeJson 返回非空字符串",
                    RegisterThen("Module/Leaf", "描述",
                        RegistryAssert.TreeJsonContains("JSON 包含 Module", "Module"),
                        RegistryAssert.TreeJsonContains("JSON 包含 Leaf", "Leaf"))),
                ("GetTreeJson 包含节点描述",
                    RegisterThen("App/Feature", "功能描述",
                        RegistryAssert.TreeJsonContains("JSON 包含描述", "功能描述"))));
        }


        [TestMethod]
        public void Test_Registry_IsRegistered()
        {
            RunGroup("LogCategoryRegistry IsRegistered",
                ("已注册的路径返回 true",
                    RegisterThen("Module/Leaf", "Leaf",
                        RegistryAssert.IsRegistered("已注册返回 true", "Module/Leaf"))),
                ("未注册的路径返回 false",
                    ClearThen(RegistryAssert.IsNotRegistered("未注册返回 false", "Unknown/Path"))),
                ("带虚拟根前缀的路径",
                    RegisterThen("Module/Leaf", "Leaf",
                        RegistryAssert.IsRegistered("带 /logs 前缀", "/logs/Module/Leaf"))));
        }

        [TestMethod]
        public void Test_Registry_GetAllPaths()
        {
            RunGroup("LogCategoryRegistry GetAllPaths",
                ("GetAllPaths 返回所有已注册路径",
                    RegisterRangeThen(
                        new[] { ("A/Leaf1", "L1"), ("A/Leaf2", "L2") },
                        AssertAllPathsContain("A/Leaf1", "A/Leaf2", "A"))),
                ("GetAllPaths 按字母排序",
                    RegisterRangeThen(
                        new[] { ("B/Leaf", "B"), ("A/Leaf", "A") },
                        RegistryAssert.PathsSorted("排序正确", "A", "A/Leaf", "B", "B/Leaf"))));
        }

        [TestMethod]
        public void Test_Registry_Count()
        {
            RunGroup("LogCategoryRegistry Count",
                ("Count 返回正确数量",
                    ClearThen(
                        RegistryAssert.CountEquals("初始 Count=0", 0),
                        RegistryAssert.Do("Register A/Leaf1", () => LogCategoryRegistry.Register("A/Leaf1", "L1")),
                        RegistryAssert.Do("Register A/Leaf2", () => LogCategoryRegistry.Register("A/Leaf2", "L2")),
                        RegistryAssert.CountEquals("Count=3（含 A 中间节点）", 3))),
                ("Clear 后 Count 为 0",
                    RegisterThen("A/Leaf", "L",
                        RegistryAssert.Do("Clear", () => LogCategoryRegistry.Clear()),
                        RegistryAssert.CountEquals("Clear 后 Count=0", 0))));
        }


        [TestMethod]
        public void Test_Registry_InitializeDefaults()
        {
            RunGroup("LogCategoryRegistry InitializeDefaults",
                ("InitializeDefaults 注册 26 个节点（含中间节点）",
                    InitDefaultsThen(RegistryAssert.CountEquals("Count=26（21 叶子 + 5 中间节点）", 26))),
                ("InitializeDefaults 注册所有 WdUI 分类",
                    InitDefaultsThen(AssertAllRegistered(
                        "WdUI/Render", "WdUI/Layout", "WdUI/Container",
                        "WdUI/Resize", "WdUI/Strategy", "WdUI/Template", "WdUI/Pipeline"))),
                ("InitializeDefaults 注册所有 FileDriven 分类",
                    InitDefaultsThen(AssertAllRegistered(
                        "FileDriven/General", "FileDriven/Command", "FileDriven/Inspector"))),
                ("InitializeDefaults 注册所有 Launcher 分类",
                    InitDefaultsThen(AssertAllRegistered(
                        "Launcher/Bootstrap", "Launcher/Init", "Launcher/Lifecycle"))),
                ("InitializeDefaults 注册所有 FunctionStorage 分类",
                    InitDefaultsThen(AssertAllRegistered(
                        "FunctionStorage/General", "FunctionStorage/Workflow",
                        "FunctionStorage/IO", "FunctionStorage/UI"))),
                ("InitializeDefaults 位标志从 16 开始",
                    InitDefaultsThen(RegistryAssert.BitFlagValue("WdUI/Render 位标志=1<<16", "WdUI/Render", (LogCategory)(1UL << 16)))));
        }


        [TestMethod]
        public void Test_Registry_ListDirectories_SubPath()
        {
            RunGroup("LogCategoryRegistry ListDirectories 子路径",
                ("ListDirectories 返回子目录列表",
                    RegisterRangeThen(
                        new[] { ("Module/Sub1/Leaf1", "L1"), ("Module/Sub1/Leaf2", "L2"), ("Module/Sub2/Leaf3", "L3") },
                        RegistryAssert.DirectoriesContain("包含 Sub1", "/logs/Module", "Sub1"),
                        RegistryAssert.DirectoriesContain("包含 Sub2", "/logs/Module", "Sub2"))),
                ("ListDirectories 叶子节点返回空",
                    RegisterThen("Module/Leaf", "Leaf",
                        RegistryAssert.DirectoriesEmpty("叶子节点返回空", "/logs/Module/Leaf"))));
        }

        [TestMethod]
        public void Test_Registry_ListDirectories_UnknownPath()
        {
            RunGroup("LogCategoryRegistry ListDirectories 未知路径",
                ("不存在的路径返回空集合",
                    RegisterThen("Module/Leaf", "Leaf",
                        RegistryAssert.DirectoriesEmpty("未知路径返回空", "/logs/Unknown"))));
        }


        [TestMethod]
        public void Test_Registry_Resolve_WithVirtualRoot()
        {
            RunGroup("LogCategoryRegistry Resolve 带虚拟根前缀",
                ("带 /logs 前缀解析成功",
                    RegisterThen("WdUI/Layout", "Layout",
                        RegistryAssert.BitFlagEquals("带 /logs 与不带前缀结果相同", "/logs/WdUI/Layout", "WdUI/Layout"))),
                ("仅 /logs 路径返回 0",
                    RegisterThen("WdUI/Layout", "Layout",
                        RegistryAssert.BitFlagZero("/logs 返回 0", "/logs"))));
        }


        [TestMethod]
        public void Test_Registry_ResolveCombined_Advanced()
        {
            RunGroup("LogCategoryRegistry ResolveCombined 高级测试",
                ("分号分隔的组合路径",
                    RegisterRangeThen(
                        new[] { ("A/Leaf1", "L1"), ("B/Leaf2", "L2") },
                        RegistryAssert.CombinedContains("组合包含 A/Leaf1 和 B/Leaf2",
                            "A/Leaf1;B/Leaf2", "A/Leaf1", "B/Leaf2"))),
                ("空字符串返回 0",
                    ClearThen(RegistryAssert.CombinedZero("空字符串返回 0", ""))),
                ("空白字符串返回 0",
                    ClearThen(RegistryAssert.CombinedZero("空白字符串返回 0", "   "))));
        }


        [TestMethod]
        public void Test_Registry_CategoryNodeProperties()
        {
            RunGroup("LogCategoryRegistry CategoryNode 属性",
                ("CategoryNode ToString 返回 FullPath",
                    ClearThen(RegistryAssert.NodeToStringEqualsFullPath("ToString=FullPath", "Module/Test", "描述"))),
                ("CategoryNode Category 可读写",
                    ClearThen(RegistryAssert.NodeCategoryReadWrite("Category 可读写", "Module/Test", "描述"))));
        }


        [TestMethod]
        public void Test_Registry_CategoryTreeToTreeString()
        {
            RunGroup("LogCategoryRegistry CategoryTree ToTreeString",
                ("ToTreeString 返回树形文本",
                    RegisterRangeThen(
                        new[] { ("Module/Leaf1", "Leaf 1"), ("Module/Leaf2", "Leaf 2") },
                        RegistryAssert.TreeStringContains("包含 Module", "Module"),
                        RegistryAssert.TreeStringContains("包含 Leaf1", "Leaf1"),
                        RegistryAssert.TreeStringContains("包含 Leaf2", "Leaf2"))));
        }
    }
}
