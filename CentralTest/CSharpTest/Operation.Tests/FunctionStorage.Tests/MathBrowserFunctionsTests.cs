
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;
using TestInfrastructure;

namespace FunctionStorage.Tests
{
    [TestClass]
    public class MathBrowserFunctionsTests : TestBase
    {
        [TestInitialize]
        public void ResetState()
        {
            Registrations.MathFunctionRegistrar.ResetRegistration();
        }

        [TestMethod]
        public void Math_GetDomains_AfterRegistration_ShouldReturnDomains()
        {
            var result = new TestGroupBuilder("GetDomains")
                .Add("应返回非空域列表", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    Console.WriteLine($"域列表: {domains}");
                    return !string.IsNullOrEmpty(domains);
                })
                .Add("应包含 Activation 域", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    return domains.Contains("Activation");
                })
                .Add("应包含 Distance 域", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    return domains.Contains("Distance");
                })
                .Add("应包含 Clustering 域", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    return domains.Contains("Clustering");
                })
                .Add("应包含 Statistics 域", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    return domains.Contains("Statistics");
                })
                .Add("应包含 Tensor 域", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetDomains", out var del) || del == null)
                        return false;

                    var domains = del.DynamicInvoke()?.ToString() ?? "";
                    return domains.Contains("Tensor");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void Math_GetFunctionsInDomain_WithValidDomain_ShouldReturnFunctions()
        {
            var result = new TestGroupBuilder("GetFunctionsInDomain")
                .Add("Activation 域应包含 ReLU", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionsInDomain", out var del) || del == null)
                        return false;

                    var functions = del.DynamicInvoke("Activation")?.ToString() ?? "";
                    Console.WriteLine($"Activation 域函数: {functions}");
                    return functions.Contains("Activation_ReLU");
                })
                .Add("Activation 域应包含 Sigmoid", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionsInDomain", out var del) || del == null)
                        return false;

                    var functions = del.DynamicInvoke("Activation")?.ToString() ?? "";
                    return functions.Contains("Activation_Sigmoid");
                })
                .Add("Distance 域应包含 Euclidean", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionsInDomain", out var del) || del == null)
                        return false;

                    var functions = del.DynamicInvoke("Distance")?.ToString() ?? "";
                    return functions.Contains("Distance_Euclidean");
                })
                .Add("不存在的域应返回空字符串", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionsInDomain", out var del) || del == null)
                        return false;

                    var functions = del.DynamicInvoke("NonExistentDomain")?.ToString() ?? "";
                    return string.IsNullOrEmpty(functions);
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void Math_GetFunctionDetail_WithValidFunction_ShouldReturnDetail()
        {
            var result = new TestGroupBuilder("GetFunctionDetail")
                .Add("Activation_ReLU 应返回包含函数名的详情", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionDetail", out var del) || del == null)
                        return false;

                    var detail = del.DynamicInvoke("Activation_ReLU")?.ToString() ?? "";
                    Console.WriteLine($"Activation_ReLU 详情:\n{detail}");
                    return detail.Contains("Activation_ReLU");
                })
                .Add("Activation_ReLU 应包含描述信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionDetail", out var del) || del == null)
                        return false;

                    var detail = del.DynamicInvoke("Activation_ReLU")?.ToString() ?? "";
                    return detail.Contains("描述") || detail.Contains("ReLU");
                })
                .Add("Activation_ReLU 应包含参数信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionDetail", out var del) || del == null)
                        return false;

                    var detail = del.DynamicInvoke("Activation_ReLU")?.ToString() ?? "";
                    return detail.Contains("参数") || detail.Contains("x");
                })
                .Add("不存在的函数应返回'函数未找到'", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetFunctionDetail", out var del) || del == null)
                        return false;

                    var detail = del.DynamicInvoke("NonExistentFunc")?.ToString() ?? "";
                    return detail.Contains("未找到");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void Math_Execute_WithValidParams_ShouldReturnResult()
        {
            var result = new TestGroupBuilder("Execute")
                .Add("Activation_ReLU|-1.0 应返回 0", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Execute", out var del) || del == null)
                        return false;

                    var execResult = del.DynamicInvoke("Activation_ReLU", "-1.0")?.ToString() ?? "";
                    Console.WriteLine($"Activation_ReLU(-1.0) = {execResult}");
                    return execResult == "0";
                })
                .Add("Activation_ReLU|0.5 应返回 0.5", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Execute", out var del) || del == null)
                        return false;

                    var execResult = del.DynamicInvoke("Activation_ReLU", "0.5")?.ToString() ?? "";
                    return execResult == "0.5";
                })
                .Add("不存在的函数应返回错误信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Execute", out var del) || del == null)
                        return false;

                    var execResult = del.DynamicInvoke("NonExistentFunc", "")?.ToString() ?? "";
                    return execResult.Contains("错误") || execResult.Contains("未找到");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void Math_Search_WithKeyword_ShouldReturnMatches()
        {
            var result = new TestGroupBuilder("Search")
                .Add("搜索 ReLU 应返回 Activation_ReLU", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Search", out var del) || del == null)
                        return false;

                    var matches = del.DynamicInvoke("ReLU")?.ToString() ?? "";
                    Console.WriteLine($"搜索 'ReLU' 结果: {matches}");
                    return matches.Contains("Activation_ReLU");
                })
                .Add("搜索 KMeans 应返回 Clustering_KMeans", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Search", out var del) || del == null)
                        return false;

                    var matches = del.DynamicInvoke("KMeans")?.ToString() ?? "";
                    return matches.Contains("Clustering_KMeans");
                })
                .Add("搜索空字符串应返回所有函数", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Search", out var del) || del == null)
                        return false;

                    var matches = del.DynamicInvoke("")?.ToString() ?? "";
                    return matches.Contains("Activation_ReLU") && matches.Contains("Distance_Euclidean");
                })
                .Add("搜索不存在的关键字应返回空字符串", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_Search", out var del) || del == null)
                        return false;

                    var matches = del.DynamicInvoke("ZZZZNotExist")?.ToString() ?? "";
                    return string.IsNullOrEmpty(matches);
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void Math_GetStats_AfterRegistration_ShouldReturnStats()
        {
            var result = new TestGroupBuilder("GetStats")
                .Add("应返回非空统计信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetStats", out var del) || del == null)
                        return false;

                    var stats = del.DynamicInvoke()?.ToString() ?? "";
                    Console.WriteLine($"统计信息: {stats}");
                    return !string.IsNullOrEmpty(stats);
                })
                .Add("应包含函数数量信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetStats", out var del) || del == null)
                        return false;

                    var stats = del.DynamicInvoke()?.ToString() ?? "";
                    return stats.Contains("个数学函数");
                })
                .Add("应包含域数量信息", () =>
                {
                    var registry = CreateFullyRegisteredRegistry();
                    if (!registry.TryGetDelegate("Math_GetStats", out var del) || del == null)
                        return false;

                    var stats = del.DynamicInvoke()?.ToString() ?? "";
                    return stats.Contains("个数学域");
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        #region 辅助方法

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

        private static IFunctionRegistry CreateFullyRegisteredRegistry()
        {
            var registry = new FunctionRegistry();

            MathLibrary.RegisterAll(registry);

            Registrations.MathFunctionRegistrar.RegisterAll(registry, GetConfigPath());

            Registrations.MathBrowserFunctions.RegisterAll(registry);

            return registry;
        }

        #endregion
    }
}
