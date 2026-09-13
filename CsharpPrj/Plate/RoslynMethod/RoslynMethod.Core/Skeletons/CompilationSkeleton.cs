using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class CompilationSkeleton
    {
        #region 编译构建

        public class CompilationConfig
        {
            public string AssemblyName { get; set; } = $"Assembly_{Guid.NewGuid():N}";
            public OutputKind OutputKind { get; set; } = OutputKind.DynamicallyLinkedLibrary;
            public OptimizationLevel OptimizationLevel { get; set; } = OptimizationLevel.Debug;
            public bool AllowUnsafe { get; set; } = false;
            public bool CheckOverflow { get; set; } = false;
            public List<MetadataReference> AdditionalReferences { get; set; } = new();
            public bool AutoResolveReferences { get; set; } = true;

            /// <summary>是否立即收集诊断（false 时跳过 <c>GetDiagnostics</c>，供只取语法树/语义模型的轻量通道使用）。</summary>
            public bool CollectDiagnostics { get; set; } = true;
        }

        public class CompilationResult
        {
            public bool Success { get; set; }
            public CSharpCompilation Compilation { get; set; }
            public SyntaxTree SyntaxTree { get; set; }
            public SyntaxNode Root { get; set; }
            public SemanticModel SemanticModel { get; set; }
            public List<Diagnostic> Diagnostics { get; set; } = new();
            public List<CompilationError> Errors { get; set; } = new();
            public MemoryStream AssemblyStream { get; set; }
            public string ErrorSummary { get; set; }
        }

        public static CompilationResult Build(string code, CompilationConfig config = null)
        {
            config ??= new CompilationConfig();
            var result = new CompilationResult();

            try
            {
                result.SyntaxTree = CSharpSourceProvider.Instance.Parse(code);
                result.Root = result.SyntaxTree.GetRoot();

                var references = new List<MetadataReference>(config.AdditionalReferences);
                if (config.AutoResolveReferences)
                {
                    references.AddRange(CompilationReferenceHelper.GetRequiredReferences(code));
                }

                var options = new CSharpCompilationOptions(
                    config.OutputKind,
                    optimizationLevel: config.OptimizationLevel,
                    allowUnsafe: config.AllowUnsafe,
                    checkOverflow: config.CheckOverflow);

                result.Compilation = CSharpCompilation.Create(
                    config.AssemblyName,
                    new[] { result.SyntaxTree },
                    references,
                    options);

                result.SemanticModel = result.Compilation.GetSemanticModel(result.SyntaxTree);

                if (config.CollectDiagnostics)
                {
                    result.Diagnostics = result.Compilation.GetDiagnostics().ToList();
                    result.Errors = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => CompilationError.FromDiagnostic(d, ErrorSeverity.CompilationError))
                        .ToList();

                    result.Success = !result.Errors.Any();
                    result.ErrorSummary = result.Errors.Any()
                        ? $"发现 {result.Errors.Count} 个编译错误"
                        : "编译成功";
                }
                else
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new CompilationError("SYSTEM", ex.Message, null, null, ErrorSeverity.SystemError));
                result.ErrorSummary = $"编译过程异常: {ex.Message}";
            }

            return result;
        }

        /// <summary>构建失败（未取得语义模型）即抛出，供要求「失败即异常」语义的调用点共用同一判定。</summary>
        public static CompilationResult BuildOrThrow(string code, CompilationConfig config = null)
        {
            var result = Build(code, config);
            if (result.SemanticModel == null)
                throw new InvalidOperationException($"编译构建失败: {result.ErrorSummary ?? "未取得语义模型"}");

            return result;
        }

        public static CompilationResult BuildAndEmit(string code, CompilationConfig config = null)
        {
            var result = Build(code, config);
            if (!result.Success) return result;

            try
            {
                result.AssemblyStream = new MemoryStream();
                var emitResult = result.Compilation.Emit(result.AssemblyStream);

                if (!emitResult.Success)
                {
                    result.Success = false;
                    result.Errors.AddRange(
                        emitResult.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => CompilationError.FromDiagnostic(d, ErrorSeverity.EmitError)));
                    result.ErrorSummary = $"Emit 失败: {result.Errors.Count} 个错误";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add(new CompilationError("EMIT", ex.Message, null, null, ErrorSeverity.EmitError));
                result.ErrorSummary = $"Emit 异常: {ex.Message}";
            }

            return result;
        }

        #endregion

        #region 批量编译

        public static List<CompilationResult> BuildMany(
            IEnumerable<string> codeSnippets,
            CompilationConfig config = null,
            bool parallel = false)
        {
            var snippets = codeSnippets.ToList();
            var results = new List<CompilationResult>();

            if (parallel)
            {
                Parallel.ForEach(snippets, code =>
                {
                    var result = Build(code, config);
                    lock (results) results.Add(result);
                });
            }
            else
            {
                foreach (var code in snippets)
                {
                    results.Add(Build(code, config));
                }
            }

            return results;
        }

        #endregion
    }
}
