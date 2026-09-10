#nullable enable
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Core;

namespace UniversalMiddleware.Stages
{
    public class VariableResolveTransform : TransformBase<string, string>
    {
        private readonly string _varPrefix;
        private readonly string _varSuffix;

        public override string Name => "VariableResolve";

        public VariableResolveTransform(string varPrefix = "${", string varSuffix = "}")
        {
            _varPrefix = varPrefix;
            _varSuffix = varSuffix;
        }

        public override Task<string> ProcessAsync(string input, JsonPipelineContext context)
        {
            if (string.IsNullOrEmpty(input))
                return Task.FromResult(input);

            var result = ResolveVariables(input, context);
            return Task.FromResult(result);
        }

        private string ResolveVariables(string input, JsonPipelineContext context)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var resolved = input;
            int startIndex = 0;
            int maxIterations = 100;
            int iteration = 0;

            while ((startIndex = resolved.IndexOf(_varPrefix, startIndex)) >= 0)
            {
                if (++iteration > maxIterations)
                {
                    context.Logger?.LogWarning(
                        $"VariableResolveTransform: 变量解析超过最大迭代次数 ({maxIterations})，" +
                        $"可能存在循环引用。输入: {input.Substring(0, Math.Min(100, input.Length))}");
                    break;
                }

                var endIndex = resolved.IndexOf(_varSuffix, startIndex + _varPrefix.Length);
                if (endIndex < 0) break;

                var varName = resolved.Substring(
                    startIndex + _varPrefix.Length,
                    endIndex - startIndex - _varPrefix.Length);

                if (context.Items.TryGetValue(varName, out var varValue) && varValue != null)
                {
                    var replacement = varValue.ToString() ?? "";
                    
                    if (replacement.Contains(_varPrefix))
                    {
                        context.Logger?.LogDebug(
                            $"VariableResolveTransform: 变量 '{varName}' 的值包含变量前缀 '{_varPrefix}'，" +
                            $"可能存在嵌套引用");
                    }

                    resolved = resolved.Substring(0, startIndex) +
                               replacement +
                               resolved.Substring(endIndex + _varSuffix.Length);
                    startIndex = 0;
                }
                else
                {
                    startIndex = endIndex + _varSuffix.Length;
                    if (startIndex >= resolved.Length) break;
                }
            }

            return resolved;
        }


        public static List<string> ResolveParameters(List<string> parameters, JsonPipelineContext context,
            string varPrefix = "${", string varSuffix = "}")
        {
            var transform = new VariableResolveTransform(varPrefix, varSuffix);
            var resolved = new List<string>();

            foreach (var param in parameters)
            {
                if (string.IsNullOrEmpty(param))
                {
                    resolved.Add(param);
                    continue;
                }

                var result = transform.ResolveVariables(param, context);
                resolved.Add(result);
            }

            return resolved;
        }
    }
}
