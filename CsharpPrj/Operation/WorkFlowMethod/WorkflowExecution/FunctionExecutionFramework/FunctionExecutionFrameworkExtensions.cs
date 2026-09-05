
using BmFunctionRegistry = TConfig.ITrConfigRegistry;
using BmExecutionResult = TConfig.ExecutionResult;
using BmExecutionContext = TConfig.ExecutionContext;
using BmIFunctionUnit = TConfig.IFunctionUnit;
using BmPipelineDefinition = TConfig.TrPipeline;
using TConfig;

namespace WorkFlowMethod.FunctionExecutionFramework
{
    public static class FunctionExecutionFrameworkExtensions
    {
        public static void RegisterAll(this BmFunctionRegistry registry,
            params (string Name, Delegate Function)[] functions)
        {
            foreach (var (name, function) in functions)
            {
                registry.Register(name, function);
            }
        }

        public static void RegisterAll(this BmFunctionRegistry registry,
            params BmIFunctionUnit[] units)
        {
            foreach (var unit in units)
            {
                registry.Register(unit.Name, (Delegate)(async (object? input, BmExecutionContext? ctx) =>
                {
                    var result = await unit.ExecuteAsync(input, ctx);
                    return result.Value;
                }));
            }
        }

        public static void Register(this BmFunctionRegistry registry, BmIFunctionUnit unit)
        {
            registry.Register(unit.Name, (Delegate)(async (object? input, BmExecutionContext? ctx) =>
            {
                var result = await unit.ExecuteAsync(input, ctx);
                return result.Value;
            }));
        }

        public static async Task<BmExecutionResult> TryExecuteAsync(
            this BmFunctionRegistry registry,
            string functionName,
            object? parameters = null,
            BmExecutionContext? context = null)
        {
            try
            {
                if (!registry.Contains(functionName))
                    return BmExecutionResult.Failure($"函数未注册: {functionName}");

                if (registry.TryGetConfig(functionName, out var config))
                {
                    var result = config.Invoke(parameters);
                    if (result is Task task)
                    {
                        await task;
                        return BmExecutionResult.Success(null);
                    }
                    return BmExecutionResult.Success(result);
                }

                return BmExecutionResult.Failure($"无法获取函数配置: {functionName}");
            }
            catch (Exception ex)
            {
                return BmExecutionResult.Failure(ex.Message, ex);
            }
        }

        public static RegistryStats GetStats(this BmFunctionRegistry registry)
        {
            var names = registry.GetRegisteredNames().ToList();
            return new RegistryStats
            {
                TotalFunctions = names.Count,
                FunctionNames = names
            };
        }
    }

    public class RegistryStats
    {
        public int TotalFunctions { get; set; }
        public List<string> FunctionNames { get; set; } = new();
    }
}
