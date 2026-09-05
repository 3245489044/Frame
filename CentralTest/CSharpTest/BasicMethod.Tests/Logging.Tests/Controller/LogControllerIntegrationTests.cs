
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Logging.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests
{
    [TestClass]
    public class LogControllerIntegrationTests : LoggingTestBase<Unit>
    {
        protected override void CleanupTest()
        {
            LogController.Reset();
        }

        [TestMethod]
        public void Test_Controller_Configure_EnablesCategories()
        {
            RunTestGroup("LogController Configure 设置全局分类", builder =>
            {
                builder.Add("Configure 后 IsOutputEnabled 返回 true", () =>
                {
                    LogController.Configure(LogCategories.WdUI_Render);

                    return LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && !LogController.IsOutputEnabled(LogCategories.WdUI_Layout);
                });

                builder.Add("Configure 多个分类后全部启用", () =>
                {
                    LogController.Configure(LogCategories.WdUI_Render | LogCategories.WdUI_Layout);

                    return LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Layout)
                        && !LogController.IsOutputEnabled(LogCategories.FileDriven_Command);
                });

                builder.Add("Configure 后 GlobalCategories 属性正确", () =>
                {
                    var expected = LogCategories.WdUI_Render | LogCategories.WdUI_Layout;
                    LogController.Configure(expected);

                    return LogController.GlobalCategories == expected;
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Parse_CommaSeparated()
        {
            RunTestGroup("LogController Parse 逗号分隔", builder =>
            {
                builder.Add("逗号分隔解析多个分类", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render,WdUI_Layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout)
                        && !parsed.HasFlag(LogCategories.FileDriven_Command);
                });

                builder.Add("逗号分隔带空格", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render, WdUI_Layout, FileDriven_Command");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout)
                        && parsed.HasFlag(LogCategories.FileDriven_Command);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Parse_SemicolonSeparated()
        {
            RunTestGroup("LogController Parse 分号分隔", builder =>
            {
                builder.Add("分号分隔解析多个分类", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render;WdUI_Layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout);
                });

                builder.Add("混合逗号和分号分隔", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render,WdUI_Layout;FileDriven_Command");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout)
                        && parsed.HasFlag(LogCategories.FileDriven_Command);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Parse_CaseInsensitive()
        {
            RunTestGroup("LogController Parse 大小写不敏感", builder =>
            {
                builder.Add("小写分类名解析", () =>
                {
                    var parsed = LogController.Parse("wdui_render,wdui_layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout);
                });

                builder.Add("混合大小写分类名解析", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render,Wdui_Layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Parse_Aliases()
        {
            RunTestGroup("LogController Parse 别名解析", builder =>
            {
                builder.Add("点号格式别名 wdui.render", () =>
                {
                    var parsed = LogController.Parse("wdui.render,wdui.layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout);
                });

                builder.Add("简短别名 render", () =>
                {
                    var parsed = LogController.Parse("render,layout");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed.HasFlag(LogCategories.WdUI_Layout);
                });

                builder.Add("FileDriven 别名 filedriven.command", () =>
                {
                    var parsed = LogController.Parse("filedriven.command,general");

                    return parsed.HasFlag(LogCategories.FileDriven_Command)
                        && parsed.HasFlag(LogCategories.FileDriven_General);
                });

                builder.Add("Launcher 别名 launcher.init", () =>
                {
                    var parsed = LogController.Parse("launcher.init,launcher.lifecycle");

                    return parsed.HasFlag(LogCategories.Launcher_Init)
                        && parsed.HasFlag(LogCategories.Launcher_Lifecycle);
                });

                builder.Add("FunctionStorage 别名 fs.workflow", () =>
                {
                    var parsed = LogController.Parse("fs.workflow,fs.general");

                    return parsed.HasFlag(LogCategories.FunctionStorage_Workflow)
                        && parsed.HasFlag(LogCategories.FunctionStorage_General);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Parse_EdgeCases()
        {
            RunTestGroup("LogController Parse 边界情况", builder =>
            {
                builder.Add("空字符串返回 None", () =>
                {
                    var parsed = LogController.Parse("");
                    return parsed == LogCategory.None;
                });

                builder.Add("空白字符串返回 None", () =>
                {
                    var parsed = LogController.Parse("   ");
                    return parsed == LogCategory.None;
                });

                builder.Add("null 字符串返回 None", () =>
                {
                    var parsed = LogController.Parse(null!);
                    return parsed == LogCategory.None;
                });

                builder.Add("未知分类名返回 None", () =>
                {
                    var parsed = LogController.Parse("UnknownCategory");
                    return parsed == LogCategory.None;
                });

                builder.Add("部分有效部分无效", () =>
                {
                    var parsed = LogController.Parse("WdUI_Render,InvalidCategory");

                    return parsed.HasFlag(LogCategories.WdUI_Render)
                        && parsed != LogCategory.None;
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_IsOutputEnabled_None()
        {
            RunTestGroup("LogController IsOutputEnabled None", builder =>
            {
                builder.Add("GlobalCategories=None 时所有分类返回 false", () =>
                {
                    LogController.Configure(LogCategory.None);

                    return !LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && !LogController.IsOutputEnabled(LogCategories.WdUI_Layout)
                        && !LogController.IsOutputEnabled(LogCategories.FileDriven_Command)
                        && !LogController.IsOutputEnabled(LogCategory.System);
                });

                builder.Add("category=None 时返回 false", () =>
                {
                    LogController.Configure(LogCategory.All);

                    return !LogController.IsOutputEnabled(LogCategory.None);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_IsOutputEnabled_All()
        {
            RunTestGroup("LogController IsOutputEnabled All", builder =>
            {
                builder.Add("GlobalCategories=All 时所有分类返回 true", () =>
                {
                    LogController.Configure(LogCategory.All);

                    return LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Layout)
                        && LogController.IsOutputEnabled(LogCategories.FileDriven_Command)
                        && LogController.IsOutputEnabled(LogCategory.System)
                        && LogController.IsOutputEnabled(LogCategories.Launcher_Init)
                        && LogController.IsOutputEnabled(LogCategories.FunctionStorage_General);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_IsOutputEnabled_CombinedFlags()
        {
            RunTestGroup("LogController IsOutputEnabled 组合标志", builder =>
            {
                builder.Add("组合标志检查子分类", () =>
                {
                    LogController.Configure(LogCategories.WdUI_All);

                    return LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Layout)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Container)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Resize)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Strategy)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Template)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Pipeline);
                });

                builder.Add("FileDriven_All 包含所有子分类", () =>
                {
                    LogController.Configure(LogCategories.FileDriven_All);

                    return LogController.IsOutputEnabled(LogCategories.FileDriven_General)
                        && LogController.IsOutputEnabled(LogCategories.FileDriven_Command)
                        && LogController.IsOutputEnabled(LogCategories.FileDriven_Inspector);
                });

                builder.Add("Launcher_All 包含所有子分类", () =>
                {
                    LogController.Configure(LogCategories.Launcher_All);

                    return LogController.IsOutputEnabled(LogCategories.Launcher_Bootstrap)
                        && LogController.IsOutputEnabled(LogCategories.Launcher_Init)
                        && LogController.IsOutputEnabled(LogCategories.Launcher_Lifecycle);
                });

                builder.Add("FunctionStorage_All 包含所有子分类", () =>
                {
                    LogController.Configure(LogCategories.FunctionStorage_All);

                    return LogController.IsOutputEnabled(LogCategories.FunctionStorage_General)
                        && LogController.IsOutputEnabled(LogCategories.FunctionStorage_Workflow)
                        && LogController.IsOutputEnabled(LogCategories.FunctionStorage_IO)
                        && LogController.IsOutputEnabled(LogCategories.FunctionStorage_UI);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Reset()
        {
            RunTestGroup("LogController Reset", builder =>
            {
                builder.Add("Reset 后 GlobalCategories 恢复为 None", () =>
                {
                    LogController.Configure(LogCategory.All);
                    LogController.Reset();

                    return LogController.GlobalCategories == LogCategory.None;
                });

                builder.Add("Reset 后 IsOutputEnabled 返回 false", () =>
                {
                    LogController.Configure(LogCategory.All);
                    LogController.Reset();

                    return !LogController.IsOutputEnabled(LogCategories.WdUI_Render);
                });
            }).AssertAllPassed();
        }

        [TestMethod]
        public void Test_Controller_Configure_OverwritesPrevious()
        {
            RunTestGroup("LogController Configure 覆盖前值", builder =>
            {
                builder.Add("多次 Configure 后只保留最后一次", () =>
                {
                    LogController.Configure(LogCategories.WdUI_Render);
                    LogController.Configure(LogCategories.WdUI_Layout);

                    return !LogController.IsOutputEnabled(LogCategories.WdUI_Render)
                        && LogController.IsOutputEnabled(LogCategories.WdUI_Layout);
                });
            }).AssertAllPassed();
        }
    }
}
