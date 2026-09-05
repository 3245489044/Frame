using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public class DynamicCompilation
    {
        public class CompilationException : Exception
        {
            public List<Diagnostic> Errors { get; }
            public string ErrorDetails { get; }

            public CompilationException(string message, List<Diagnostic> errors, string errorDetails) : base(message)
            {
                Errors = errors;
                ErrorDetails = errorDetails;
            }
        }

        public static CSharpCompilation BuildCompilation(string code)
        {
            var config = new CompilationSkeleton.CompilationConfig
            {
                OptimizationLevel = OptimizationLevel.Release
            };

            var currentAssemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(currentAssemblyLocation))
            {
                config.AdditionalReferences.Add(MetadataReference.CreateFromFile(currentAssemblyLocation));
            }

            var result = CompilationSkeleton.Build(code, config);
            if (!result.Success)
            {
                throw new CompilationException(
                    $"动态编译失败，共 {result.Errors.Count} 个错误",
                    result.Diagnostics,
                    $"编译失败，发现 {result.Errors.Count} 个错误:\n{string.Join("\n", result.Errors.Select(e => $"• {e.Message} ({e.Id})"))}");
            }
            return result.Compilation;
        }

        public static async Task<Assembly> CompileAssembly(string code)
        {
            var config = new CompilationSkeleton.CompilationConfig
            {
                OptimizationLevel = OptimizationLevel.Release
            };

            var currentAssemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(currentAssemblyLocation))
            {
                config.AdditionalReferences.Add(MetadataReference.CreateFromFile(currentAssemblyLocation));
            }

            var result = CompilationSkeleton.BuildAndEmit(code, config);
            if (!result.Success)
            {
                throw new CompilationException(
                    $"动态编译失败，共 {result.Errors.Count} 个错误",
                    result.Diagnostics,
                    $"编译失败，发现 {result.Errors.Count} 个错误:\n{string.Join("\n", result.Errors.Select(e => $"• {e.Message} ({e.Id})"))}");
            }

            result.AssemblyStream.Seek(0, SeekOrigin.Begin);
            return Assembly.Load(result.AssemblyStream.ToArray());
        }

        public static Task<Assembly> CompileAndExecute(CSharpCompilation compilation, object executeParameter = null, Action<Assembly, object> executeAction = null)
        {
            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);
            if (!emitResult.Success)
                return Task.FromResult<Assembly>(null);

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            executeAction?.Invoke(assembly, executeParameter);
            return Task.FromResult(assembly);
        }

        public static void ClearCache()
        {
            CompilationReferenceHelper.ClearCache();
        }
    }
}
