using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class ConfigPlaceholderHandlerTests : TestBase
    {
        private const string TestConfigPath = "test_parameters.json";

        [TestMethod]
        public void RunAll_ConfigPlaceholderHandler()
        {
            var result = RunTestGroup("ConfigPlaceholderHandler", builder =>
            {
                builder.Add("CanHandle_ConfigPrefix_ShouldReturnTrue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.CanHandle(Placeholder.Input("config:app.name"));
                });

                builder.Add("CanHandle_SettingPrefix_ShouldReturnTrue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.CanHandle(Placeholder.Input("setting:app.name"));
                });

                builder.Add("CanHandle_AppSettingsPrefix_ShouldReturnTrue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.CanHandle(Placeholder.Input("appsettings:app.name"));
                });

                builder.Add("CanHandle_OtherPrefix_ShouldReturnFalse", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return !handler.CanHandle(Placeholder.Input("log:test"));
                });

                builder.Add("CanHandle_EmptyPrefix_ShouldReturnFalse", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return !handler.CanHandle(Placeholder.Input(""));
                });

                builder.Add("Handle_ShouldStoreConfigValueInContext", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("ConfigModule");
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var ph = Placeholder.Input("config:app.name");

                    handler.Handle(ph, null, ctx, "batch-001");

                    var value = ctx.GetData<string>("__Config_app_name");
                    return value == "TestApp";
                });

                builder.Add("Handle_WithResolvedValue_ShouldUseResolvedValue", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("ConfigModule");
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var ph = Placeholder.Input("config:app.name");

                    handler.Handle(ph, "OverriddenValue", ctx, "batch-001");

                    var value = ctx.GetData<string>("__Config_app_name");
                    return value == "OverriddenValue";
                });

                builder.Add("Handle_NonExistentPath_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("ConfigModule");
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var ph = Placeholder.Input("config:non.existent.path");

                    handler.Handle(ph, null, ctx, "batch-001");
                    return true;
                });

                builder.Add("Handle_SettingPrefix_ShouldStoreConfigValue", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("ConfigModule");
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var ph = Placeholder.Input("setting:database.host");

                    handler.Handle(ph, null, ctx, "batch-001");

                    var value = ctx.GetData<string>("__Config_database_host");
                    return value == "localhost";
                });

                builder.Add("Handle_AppSettingsPrefix_ShouldStoreConfigValue", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("ConfigModule");
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var ph = Placeholder.Input("appsettings:app.version");

                    handler.Handle(ph, null, ctx, "batch-001");

                    var value = ctx.GetData<string>("__Config_app_version");
                    return value == "1.0.0";
                });

                builder.Add("GetString_ExistingPath_ShouldReturnValue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetString("app.name") == "TestApp";
                });

                builder.Add("GetString_NonExistentPath_ShouldReturnDefault", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetString("non.existent", "default-val") == "default-val";
                });

                builder.Add("GetInt_ExistingPath_ShouldReturnValue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetInt("database.port") == 5432;
                });

                builder.Add("GetInt_NonExistentPath_ShouldReturnDefault", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetInt("non.existent", 999) == 999;
                });

                builder.Add("GetDouble_ExistingPath_ShouldReturnValue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return Math.Abs(handler.GetDouble("database.timeout") - 30.5) < 0.01;
                });

                builder.Add("GetDouble_NonExistentPath_ShouldReturnDefault", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return Math.Abs(handler.GetDouble("non.existent", 1.5) - 1.5) < 0.01;
                });

                builder.Add("GetBool_ExistingPath_ShouldReturnValue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetBool("app.debug") == true;
                });

                builder.Add("GetBool_NonExistentPath_ShouldReturnDefault", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetBool("non.existent", true) == true;
                });

                builder.Add("GetDoubleArray_ShouldReturnArray", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var arr = handler.GetDoubleArray("features.items");
                    return arr.Length == 5
                        && Math.Abs(arr[0] - 1.0) < 0.01
                        && Math.Abs(arr[4] - 5.0) < 0.01;
                });

                builder.Add("GetDoubleArray_NonExistent_ShouldReturnNull", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetDoubleArray("non.existent") == null;
                });

                builder.Add("GetStringArray_ShouldReturnArray", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    var arr = handler.GetStringArray("features.names");
                    return arr.Length == 3
                        && arr[0] == "alpha"
                        && arr[2] == "gamma";
                });

                builder.Add("GetStringArray_NonExistent_ShouldReturnNull", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetStringArray("non.existent") == null;
                });

                builder.Add("Exists_ExistingPath_ShouldReturnTrue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.Exists("app.name");
                });

                builder.Add("Exists_NonExistentPath_ShouldReturnFalse", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return !handler.Exists("non.existent.path");
                });

                builder.Add("GetString_NestedPath_ShouldReturnDeepValue", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    return handler.GetString("nested.level1.level2.value") == "deep-value";
                });

                builder.Add("Reload_ShouldNotThrow", () =>
                {
                    using var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    handler.Reload();
                    return handler.GetString("app.name") == "TestApp";
                });

                builder.Add("Dispose_ShouldNotThrow", () =>
                {
                    var handler = new ConfigPlaceholderHandler(TestConfigPath);
                    handler.Dispose();
                    return true;
                });
            });

            result.AssertAllPassed();
        }
    }
}
