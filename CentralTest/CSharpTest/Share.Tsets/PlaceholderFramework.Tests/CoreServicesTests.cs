using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class CoreServicesTests : TestBase
    {
        [TestMethod]
        public void RunAll_PlaceholderLogHelper()
        {
            var result = RunTestGroup("PlaceholderLogHelper", builder =>
            {
                builder.Add("LogInfo_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogInfo("system:startup", "TestModule", "Test info message");
                    return true;
                });

                builder.Add("LogWarn_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogWarn("system:startup", "TestModule", "Test warning message");
                    return true;
                });

                builder.Add("LogError_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogError("system:startup", "TestModule", "Test error message");
                    return true;
                });

                builder.Add("LogDebug_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogDebug("system:startup", "TestModule", "Test debug message");
                    return true;
                });

                builder.Add("LogInfo_NullCategory_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogInfo(null, "TestModule", "Test info with null category");
                    return true;
                });

                builder.Add("LogInfo_NullModule_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogInfo("system:startup", null, "Test info with null module");
                    return true;
                });

                builder.Add("LogInfo_NullMessage_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogInfo("system:startup", "TestModule", null);
                    return true;
                });

                builder.Add("LogInfo_EmptyStrings_ShouldNotThrow", () =>
                {
                    PlaceholderLogHelper.LogInfo("", "", "");
                    return true;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderTemplateFormatter()
        {
            var result = RunTestGroup("PlaceholderTemplateFormatter", builder =>
            {
                builder.Add("Format_SimpleTemplate_ShouldReturnFormattedString", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { name = "World" });
                    var resultStr = PlaceholderTemplateFormatter.Format("Hello, {name}!", parameters);
                    return resultStr == "Hello, World!";
                });

                builder.Add("Format_MultiplePlaceholders_ShouldReplaceAll", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { a = "1", b = "2", c = "3" });
                    var resultStr = PlaceholderTemplateFormatter.Format("{a} + {b} = {c}", parameters);
                    return resultStr == "1 + 2 = 3";
                });

                builder.Add("Format_NoPlaceholders_ShouldReturnOriginal", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { });
                    var resultStr = PlaceholderTemplateFormatter.Format("Plain text without placeholders", parameters);
                    return resultStr == "Plain text without placeholders";
                });

                builder.Add("Format_NullTemplate_ShouldReturnEmpty", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { a = "1" });
                    var resultStr = PlaceholderTemplateFormatter.Format(null, parameters);
                    return resultStr == "";
                });

                builder.Add("Format_EmptyTemplate_ShouldReturnEmpty", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { a = "1" });
                    var resultStr = PlaceholderTemplateFormatter.Format("", parameters);
                    return resultStr == "";
                });

                builder.Add("Format_NullParameters_ShouldReturnOriginal", () =>
                {
                    var resultStr = PlaceholderTemplateFormatter.Format("Test {key}", null);
                    return resultStr == "Test {key}";
                });

                builder.Add("Format_UnmatchedPlaceholder_ShouldKeepOriginal", () =>
                {
                    var parameters = PlaceholderTemplateFormatter.ConvertParameters(new { known = "test" });
                    var resultStr = PlaceholderTemplateFormatter.Format("Value: {unknown}", parameters);
                    return resultStr == "Value: {unknown}";
                });

                builder.Add("Format_WithDictionary_ShouldWork", () =>
                {
                    var dict = new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "42" };
                    var resultStr = PlaceholderTemplateFormatter.Format("{key1}={key2}", dict);
                    return resultStr == "value1=42";
                });

                builder.Add("ConvertParameters_Null_ShouldReturnNull", () =>
                {
                    return PlaceholderTemplateFormatter.ConvertParameters(null) == null;
                });

                builder.Add("ConvertParameters_AnonymousObject_ShouldReturnDictionary", () =>
                {
                    var dict = PlaceholderTemplateFormatter.ConvertParameters(new { name = "test", value = "123" });
                    return dict != null && dict["name"] == "test" && dict["value"] == "123";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_LogCategoryResolver()
        {
            var result = RunTestGroup("LogCategoryResolver", builder =>
            {
                builder.Add("Resolve_SystemCategory_ShouldReturnSystem", () =>
                {
                    var category = LogCategoryResolver.Resolve("system:startup");
                    return category == LogCategory.System;
                });

                builder.Add("Resolve_ConfigurationCategory_ShouldReturnConfiguration", () =>
                {
                    var category = LogCategoryResolver.Resolve("configuration");
                    return category == LogCategory.Configuration;
                });

                builder.Add("Resolve_DatabaseCategory_ShouldReturnDatabase", () =>
                {
                    var category = LogCategoryResolver.Resolve("database");
                    return category == LogCategory.Database;
                });

                builder.Add("Resolve_NetworkCategory_ShouldReturnNetwork", () =>
                {
                    var category = LogCategoryResolver.Resolve("network");
                    return category == LogCategory.Network;
                });

                builder.Add("Resolve_SecurityCategory_ShouldReturnSecurity", () =>
                {
                    var category = LogCategoryResolver.Resolve("security");
                    return category == LogCategory.Security;
                });

                builder.Add("Resolve_PerformanceCategory_ShouldReturnPerformance", () =>
                {
                    var category = LogCategoryResolver.Resolve("performance");
                    return category == LogCategory.Performance;
                });

                builder.Add("Resolve_MemoryCategory_ShouldReturnMemory", () =>
                {
                    var category = LogCategoryResolver.Resolve("memory");
                    return category == LogCategory.Memory;
                });

                builder.Add("Resolve_ThreadingCategory_ShouldReturnThreading", () =>
                {
                    var category = LogCategoryResolver.Resolve("threading");
                    return category == LogCategory.Threading;
                });

                builder.Add("Resolve_UnknownCategory_ShouldReturnSystem", () =>
                {
                    var category = LogCategoryResolver.Resolve("unknown:test");
                    return category == LogCategory.System;
                });

                builder.Add("Resolve_EmptyInput_ShouldReturnSystem", () =>
                {
                    var category = LogCategoryResolver.Resolve("");
                    return category == LogCategory.System;
                });

                builder.Add("Resolve_NullInput_ShouldReturnSystem", () =>
                {
                    var category = LogCategoryResolver.Resolve(null);
                    return category == LogCategory.System;
                });

                builder.Add("Resolve_WhitespaceInput_ShouldReturnSystem", () =>
                {
                    var category = LogCategoryResolver.Resolve("   ");
                    return category == LogCategory.System;
                });

                builder.Add("Resolve_WdUIRender_ShouldReturnWdUIRender", () =>
                {
                    var category = LogCategoryResolver.Resolve("wdui.render");
                    return category == (LogCategory)LogCategoryEx.WdUI_Render;
                });

                builder.Add("Resolve_FileDrivenGeneral_ShouldReturnFileDrivenGeneral", () =>
                {
                    var category = LogCategoryResolver.Resolve("filedriven.general");
                    return category == (LogCategory)LogCategoryEx.FileDriven_General;
                });

                builder.Add("Resolve_LauncherBootstrap_ShouldReturnLauncherBootstrap", () =>
                {
                    var category = LogCategoryResolver.Resolve("launcher.bootstrap");
                    return category == (LogCategory)LogCategoryEx.Launcher_Bootstrap;
                });

                builder.Add("Resolve_FunctionStorageWorkflow_ShouldReturnFunctionStorageWorkflow", () =>
                {
                    var category = LogCategoryResolver.Resolve("fs.workflow");
                    return category == (LogCategory)LogCategoryEx.FunctionStorage_Workflow;
                });

                builder.Add("Resolve_BatchLifecycle_ShouldReturnBatchLifecycle", () =>
                {
                    var category = LogCategoryResolver.Resolve("batch.lifecycle");
                    return category == (LogCategory)LogCategoryEx.Batch_Lifecycle;
                });

                builder.Add("GetModuleName_System_ShouldReturnSystem", () =>
                {
                    var module = LogCategoryResolver.GetModuleName(LogCategory.System);
                    return module == "System";
                });

                builder.Add("GetModuleName_WdUI_ShouldReturnWdUI", () =>
                {
                    var module = LogCategoryResolver.GetModuleName((LogCategory)LogCategoryEx.WdUI_Render);
                    return module == "WdUI";
                });

                builder.Add("GetModuleName_FileDriven_ShouldReturnFileDriven", () =>
                {
                    var module = LogCategoryResolver.GetModuleName((LogCategory)LogCategoryEx.FileDriven_General);
                    return module == "FileDriven";
                });

                builder.Add("GetModuleName_Launcher_ShouldReturnLauncher", () =>
                {
                    var module = LogCategoryResolver.GetModuleName((LogCategory)LogCategoryEx.Launcher_Bootstrap);
                    return module == "Launcher";
                });

                builder.Add("GetModuleName_FunctionStorage_ShouldReturnFunctionStorage", () =>
                {
                    var module = LogCategoryResolver.GetModuleName((LogCategory)LogCategoryEx.FunctionStorage_General);
                    return module == "FunctionStorage";
                });

                builder.Add("GetModuleName_Batch_ShouldReturnBatch", () =>
                {
                    var module = LogCategoryResolver.GetModuleName((LogCategory)LogCategoryEx.Batch_Lifecycle);
                    return module == "Batch";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderMetadata()
        {
            var result = RunTestGroup("PlaceholderMetadata", builder =>
            {
                builder.Add("FromPlaceholder_ShouldSetProperties", () =>
                {
                    var ph = Placeholder.Input("config:app.name");
                    var meta = PlaceholderMetadata.FromPlaceholder(ph, "TestApp", "TestModule", "TestMethod", "batch-001", "Config");

                    return meta.Kind == ph.Kind.ToString()
                        && meta.Key == "config:app.name"
                        && meta.ResolvedValue == "TestApp"
                        && meta.SourceModule == "TestModule"
                        && meta.SourceMember == "TestMethod"
                        && meta.BatchId == "batch-001"
                        && meta.HandlerName == "Config"
                        && meta.Timestamp != default
                        && !string.IsNullOrEmpty(meta.MetadataId);
                });

                builder.Add("FromPlaceholder_WithNullOptionalParams_ShouldNotThrow", () =>
                {
                    var ph = Placeholder.Input("test:value");
                    var meta = PlaceholderMetadata.FromPlaceholder(ph, "value");

                    return meta.Kind == ph.Kind.ToString()
                        && meta.Key == "test:value"
                        && meta.ResolvedValue == "value";
                });

                builder.Add("ToString_ShouldReturnFormattedString", () =>
                {
                    var ph = Placeholder.Input("test:value");
                    var meta = PlaceholderMetadata.FromPlaceholder(ph, "resolved", batchId: "batch-001");
                    var str = meta.ToString();
                    return str.Contains("test:value") && str.Contains("resolved") && str.Contains("batch-001");
                });

                builder.Add("DefaultConstructor_ShouldSetDefaults", () =>
                {
                    var meta = new PlaceholderMetadata();
                    return meta.Kind == null
                        && meta.Key == null
                        && meta.Timestamp == default;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_HandlersService()
        {
            var result = RunTestGroup("HandlersService", builder =>
            {
                builder.Add("RegisterAndFindByName_ShouldReturnRegisteredHandler", () =>
                {
                    var service = new HandlersService();
                    var handler = new MockPlaceholderHandler();
                    service.Register(handler);

                    var retrieved = service.FindByName("mock");
                    return retrieved == handler;
                });

                builder.Add("Register_Null_ShouldThrow", () =>
                {
                    var service = new HandlersService();
                    try
                    {
                        service.Register(null);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("FindByName_NotRegistered_ShouldReturnNull", () =>
                {
                    var service = new HandlersService();
                    return service.FindByName("non-existent") == null;
                });

                builder.Add("Handlers_Empty_ShouldReturnEmptyList", () =>
                {
                    var service = new HandlersService();
                    var handlers = service.Handlers;
                    return handlers != null && handlers.Count == 0;
                });

                builder.Add("Handlers_WithRegistrations_ShouldReturnAll", () =>
                {
                    var service = new HandlersService();
                    service.Register(new MockPlaceholderHandler());
                    service.Register(new MockPlaceholderHandler2());

                    var handlers = service.Handlers;
                    return handlers.Count == 2;
                });

                builder.Add("Remove_ShouldRemoveFromRegistry", () =>
                {
                    var service = new HandlersService();
                    var handler = new MockPlaceholderHandler();
                    service.Register(handler);
                    service.Remove("mock");

                    return service.FindByName("mock") == null;
                });

                builder.Add("Remove_NotRegistered_ShouldReturnFalse", () =>
                {
                    var service = new HandlersService();
                    return service.Remove("non-existent") == false;
                });

                builder.Add("Clear_ShouldRemoveAllHandlers", () =>
                {
                    var service = new HandlersService();
                    service.Register(new MockPlaceholderHandler());
                    service.Register(new MockPlaceholderHandler2());
                    service.Clear();

                    return service.Handlers.Count == 0;
                });

                builder.Add("RegisterDefaults_ShouldRegisterAllDefaultHandlers", () =>
                {
                    var service = new HandlersService();
                    service.RegisterDefaults();
                    return service.Handlers.Count == 7;
                });

                builder.Add("RegisterRange_ShouldRegisterMultipleHandlers", () =>
                {
                    var service = new HandlersService();
                    service.RegisterRange(new IPlaceholderHandler[]
                    {
                        new MockPlaceholderHandler(),
                        new MockPlaceholderHandler2()
                    });
                    return service.Handlers.Count == 2;
                });

                builder.Add("FindHandler_ShouldFindMatchingHandler", () =>
                {
                    var service = new HandlersService();
                    service.Register(new MockPlaceholderHandler());
                    var ph = Placeholder.Input("mock:test");
                    return service.FindHandler(ph) != null;
                });

                builder.Add("FindHandler_NoMatch_ShouldReturnNull", () =>
                {
                    var service = new HandlersService();
                    var ph = Placeholder.Input("unknown:test");
                    return service.FindHandler(ph) == null;
                });

                builder.Add("Count_ShouldReturnCorrectNumber", () =>
                {
                    var service = new HandlersService();
                    service.Register(new MockPlaceholderHandler());
                    service.Register(new MockPlaceholderHandler2());
                    return service.Count == 2;
                });
            });

            result.AssertAllPassed();
        }
    }

    internal class MockPlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "mock";

        public bool CanHandle(Placeholder placeholder) => true;

        public void Handle(Placeholder placeholder, string resolvedValue, SkeletonContext context, string batchId)
        {
        }
    }

    internal class MockPlaceholderHandler2 : IPlaceholderHandler
    {
        public string Name => "mock2";

        public bool CanHandle(Placeholder placeholder) => true;

        public void Handle(Placeholder placeholder, string resolvedValue, SkeletonContext context, string batchId)
        {
        }
    }
}
