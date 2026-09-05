using Microsoft.CodeAnalysis;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public static class DynamicMethodRunner
    {
        public static async Task<Dictionary<string, object>> RunDynamicMethodsAsync(string code, string methodNameFilter = null)
        {
            try
            {
                var assembly = await CompileAssemblyAsync(code);
                return ExecuteMethodsFromAssembly(assembly, methodNameFilter);
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["CompilationError"] = GetDetailedExceptionMessage(ex)
                };
            }
        }

        public static Dictionary<string, object> RunDynamicMethods(string code, string methodNameFilter = null)
        {
            return RunDynamicMethodsAsync(code, methodNameFilter).GetAwaiter().GetResult();
        }

        private static async Task<Assembly> CompileAssemblyAsync(string sourceCode)
        {
            return await Task.Run(() =>
            {
                var config = new CompilationSkeleton.CompilationConfig
                {
                    OptimizationLevel = OptimizationLevel.Release,
                    AllowUnsafe = false
                };

                var result = CompilationSkeleton.BuildAndEmit(sourceCode, config);
                if (!result.Success)
                {
                    var errors = string.Join(Environment.NewLine,
                        result.Errors.Select(e => e.ToString()));
                    throw new InvalidOperationException($"编译失败: {errors}");
                }

                result.AssemblyStream.Seek(0, System.IO.SeekOrigin.Begin);
                return Assembly.Load(result.AssemblyStream.ToArray());
            });
        }

        private static Dictionary<string, object> ExecuteMethodsFromAssembly(Assembly assembly, string methodNameFilter)
        {
            var results = new Dictionary<string, object>();

            var methods = assembly.GetExportedTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => string.IsNullOrEmpty(methodNameFilter) ||
                            m.Name.Contains(methodNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var method in methods)
            {
                string key = method.Name;

                try
                {
                    object instance = null;
                    if (!method.IsStatic)
                    {
                        try
                        {
                            instance = Activator.CreateInstance(method.DeclaringType);
                        }
                        catch (Exception ex)
                        {
                            results[key] = $"实例创建失败: {ex.Message}";
                            continue;
                        }
                    }

                    object result = method.Invoke(instance, GetDefaultParameters(method));
                    results[key] = result;
                }
                catch (Exception ex)
                {
                    results[key] = $"执行异常: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return results;
        }

        private static object[] GetDefaultParameters(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return null;

            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                    args[i] = parameters[i].DefaultValue;
                else
                    args[i] = GetDefault(parameters[i].ParameterType);
            }
            return args;
        }

        private static object GetDefault(Type type)
        {
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            return null;
        }

        private static string GetDetailedExceptionMessage(Exception ex)
        {
            return ex.ToString();
        }
    }
}
