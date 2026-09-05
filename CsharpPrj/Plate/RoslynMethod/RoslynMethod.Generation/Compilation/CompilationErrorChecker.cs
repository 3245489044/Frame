using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public static class CompilationErrorChecker
    {
        public static async Task<(bool IsSuccess, List<Core.Models.CompilationError> Errors)> TryCompileAsync(string code)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var result = CompilationSkeleton.Build(code);
                    return (result.Success, result.Errors);
                }
                catch (Exception ex)
                {
                    return (false, new List<Core.Models.CompilationError>
                    {
                        new("EXCEPTION", $"编译过程异常: {ex.Message}", null, null, Core.Models.ErrorSeverity.SystemError)
                    });
                }
            }).ConfigureAwait(false);
        }

        public static async Task<CompilationErrorReport> GetErrorReportAsync(string code)
        {
            var (ok, errors) = await TryCompileAsync(code).ConfigureAwait(false);
            var summary = ok
                ? "编译成功，没有错误。"
                : $"发现 {errors.Count} 个错误:\n{string.Join("\n", errors.GroupBy(e => e.Severity).Select(g => $"{g.Key}: {g.Count()} 个"))}";

            var report = new CompilationErrorReport(ok, errors.Count, errors.Select(e => new CompilationError(e.Id, e.Message, e.Line, e.Column, (ErrorSeverity)e.Severity)).ToList(), summary);
            if (!ok)
            {
                report.ErrorsByLine = errors
                    .Where(e => e.Line.HasValue)
                    .GroupBy(e => e.Line.Value)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => new LineError(
                            e.Message,
                            e.Column ?? 0,
                            (ErrorSeverity)e.Severity)).ToList());
            }
            return report;
        }

        public static async Task<(bool IsSuccess, Assembly Assembly, List<Core.Models.CompilationError> Errors)> TryCompileAssemblyAsync(string code)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var result = CompilationSkeleton.BuildAndEmit(code);
                    if (!result.Success)
                        return (false, null, result.Errors);

                    result.AssemblyStream.Seek(0, SeekOrigin.Begin);
                    var assembly = Assembly.Load(result.AssemblyStream.ToArray());
                    return (true, assembly, new List<Core.Models.CompilationError>());
                }
                catch (Exception ex)
                {
                    return (false, null, new List<Core.Models.CompilationError>
                    {
                        new("LOAD_EXCEPTION", $"程序集加载失败: {ex.Message}", null, null, Core.Models.ErrorSeverity.SystemError)
                    });
                }
            }).ConfigureAwait(false);
        }

        public static async Task<List<Core.Models.CompilationError>> ValidateSnippetAsync(string snippet)
        {
            try
            {
                var wrapper = $@"using System;public class SnippetValidator{{public void TestMethod(){{{snippet}}}}}";
                var (_, errors) = await TryCompileAsync(wrapper).ConfigureAwait(false);
                return errors;
            }
            catch
            {
                return new List<Core.Models.CompilationError>
                {
                    new("VALIDATION_ERROR", "代码片段验证失败", null, null, Core.Models.ErrorSeverity.SyntaxError)
                };
            }
        }

        public static async Task<List<LineError>> GetLineErrorsAsync(string code, int line)
        {
            var report = await GetErrorReportAsync(code).ConfigureAwait(false);
            return report.ErrorsByLine.TryGetValue(line, out var errors) ? errors : new List<LineError>();
        }

        public static async Task<ErrorStatistics> GetErrorStatisticsAsync(string code)
        {
            var (_, errors) = await TryCompileAsync(code).ConfigureAwait(false);
            return new ErrorStatistics(
                errors.Count,
                errors.Count(e => e.Severity == Core.Models.ErrorSeverity.SyntaxError),
                errors.Count(e => e.Severity == Core.Models.ErrorSeverity.SemanticError),
                errors.Count(e => e.Severity == Core.Models.ErrorSeverity.CompilationError),
                errors.Where(e => e.Line.HasValue).Select(e => e.Line.Value).Distinct().Count());
        }
    }
}
