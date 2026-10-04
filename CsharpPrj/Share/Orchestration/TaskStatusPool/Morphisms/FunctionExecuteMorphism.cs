
#nullable enable
using TConfig;
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Morphisms
{
    public class FunctionExecuteMorphism : IOrderedMorphism<IPoolContext, IPoolContext>
    {
        private readonly IEventExecutionService _executionService;

        public string Name => "FunctionExecute";

        public int Order => 2;

        public FunctionExecuteMorphism(IEventExecutionService executionService)
        {
            _executionService = PortGuard.Required(executionService, nameof(executionService));
        }

        public IPoolContext Morph(IPoolContext input)
        {
            var functionName = input.Get<string>("_functionName");
            if (string.IsNullOrEmpty(functionName))
            {
                input.Set("_resultSuccess", false);
                input.Set("_errorMessage", "未指定函数名称");
                return input;
            }

            var paramValue = input.Get<object>("_paramValue");
            var paramName = input.Get<string>("_paramName");

            try
            {
                ExecutionResult result;

                if (!string.IsNullOrEmpty(paramName))
                {
                    var namedParams = new Dictionary<string, object>();
                    if (paramValue != null)
                        namedParams[paramName] = paramValue;

                    result = Task.Run(() => _executionService.ExecuteFunctionAsync(functionName, namedParams))
                        .GetAwaiter().GetResult();
                }
                else
                {
                    var positionalParams = new Dictionary<string, object?>();
                    if (paramValue != null)
                        positionalParams["param0"] = paramValue;
                    result = Task.Run(() => _executionService.ExecuteFunctionAsync(functionName, positionalParams))
                        .GetAwaiter().GetResult();
                }

                input.Set("_resultSuccess", result.IsSuccess);
                input.Set("_result", result.Value);

                if (!result.IsSuccess)
                    input.Set("_errorMessage", result.ErrorMessage ?? "函数执行失败");
            }
            catch (Exception ex)
            {
                input.Set("_resultSuccess", false);
                input.Set("_errorMessage", $"函数执行异常: {ex.Message}");
                input.Set("_result", null);
            }

            return input;
        }
    }
}
