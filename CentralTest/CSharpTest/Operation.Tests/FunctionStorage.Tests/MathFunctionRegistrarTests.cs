
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;
using Share.Registry.AutoRegister;
using TestInfrastructure;

namespace FunctionStorage.Tests
{
    [TestClass]
    public class MathFunctionRegistrarTests : TestBase
    {
        [TestInitialize]
        public void ResetState()
        {
            Registrations.MathFunctionRegistrar.ResetRegistration();
        }

        [TestMethod]
        public void RegisterAll_ConfigNotFound_ShouldSkipGracefully()
        {
            var result = new TestGroupBuilder("ConfigNotFound")
                .Add("调用 RegisterAll 不应抛出异常", () =>
                {
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry);
                    return true;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RegisterAll_EmptyConfig_ShouldSkipGracefully()
        {
            var result = new TestGroupBuilder("EmptyConfig")
                .Add("空配置不应注册任何函数", () =>
                {
                    var registry = new FunctionRegistry();
                    var configPath = CreateTempConfig("{}");
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    var count = registry.GetRegisteredNames().Count();
                    return count == 0;
                })
                .Add("Registrations 为空数组不应注册任何函数", () =>
                {
                    var registry = new FunctionRegistry();
                    var configPath = CreateTempConfig(
                        @"{ ""registrations"": [] }");
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    var count = registry.GetRegisteredNames().Count();
                    return count == 0;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        private static string GetConfigPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Registrations", "math-register-config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Operation", "FunctionStorage", "Registrations", "math-register-config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Registrations", "math-register-config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "Operation", "FunctionStorage", "Registrations", "math-register-config.json"),
            };

            foreach (var path in candidates)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            throw new FileNotFoundException("找不到 math-register-config.json 配置文件");
        }

        [TestMethod]
        public void RegisterAll_WithValidConfig_ShouldRegisterFunctions()
        {
            var configPath = GetConfigPath();

            var result = new TestGroupBuilder("ValidConfig")
                .Add("注册后注册表应包含函数", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    var names = registry.GetRegisteredNames().ToList();
                    Console.WriteLine($"已注册函数数: {names.Count}");
                    return names.Count > 0;
                })
                .Add("应包含 Activation_ReLU", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Activation_ReLU");
                })
                .Add("应包含 Distance_Euclidean", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Distance_Euclidean");
                })
                .Add("应包含 Clustering_KMeans", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Clustering_KMeans");
                })
                .Add("应包含 Statistics_Entropy", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Statistics_Entropy");
                })
                .Add("应包含 Tensor_Zeros", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Tensor_Zeros");
                })
                .Add("应包含 Similarity_Cosine", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return registry.Contains("Similarity_Cosine");
                })
                .Add("不应包含 Object 继承方法 (GetType)", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return !registry.Contains("GetType");
                })
                .Add("不应包含 Object 继承方法 (ToString)", () =>
                {
                    Registrations.MathFunctionRegistrar.ResetRegistration();
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    return !registry.Contains("ToString");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RegisterAll_DuplicateCall_ShouldSkip()
        {
            var configPath = GetConfigPath();
            var result = new TestGroupBuilder("DuplicateCall")
                .Add("第二次调用不应增加注册数量", () =>
                {
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    var firstCount = registry.GetRegisteredNames().Count();

                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);
                    var secondCount = registry.GetRegisteredNames().Count();

                    return firstCount == secondCount;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RegisteredFunctions_ShouldBeExecutable()
        {
            var configPath = GetConfigPath();
            var result = new TestGroupBuilder("Executable")
                .Add("Activation_ReLU(-1.0) 应返回 0", () =>
                {
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);

                    if (!registry.TryGetDescriptor("Activation_ReLU", out var desc))
                        return false;

                    if (!registry.TryGetDelegate("Activation_ReLU", out var del) || del == null)
                        return false;

                    var resultValue = del.DynamicInvoke("-1.0");
                    return resultValue?.ToString() == "0";
                })
                .Add("Activation_ReLU(0.5) 应返回 0.5", () =>
                {
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);

                    if (!registry.TryGetDelegate("Activation_ReLU", out var del) || del == null)
                        return false;

                    var resultValue = del.DynamicInvoke("0.5");
                    return resultValue?.ToString() == "0.5";
                })
                .Add("Distance_Euclidean 应可执行", () =>
                {
                    var registry = new FunctionRegistry();
                    Registrations.MathFunctionRegistrar.RegisterAll(registry, configPath);

                    if (!registry.TryGetDelegate("Distance_Euclidean", out var del) || del == null)
                        return false;

                    var resultValue = del.DynamicInvoke("[[0,0],[3,4]]");
                    return resultValue != null && !resultValue.ToString()!.StartsWith("错误");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        #region 辅助方法

        private static string CreateTempConfig(string jsonContent)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"MathFuncTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var configPath = Path.Combine(tempDir, "math-register-config.json");
            File.WriteAllText(configPath, jsonContent);
            return configPath;
        }

        #endregion
    }
}
