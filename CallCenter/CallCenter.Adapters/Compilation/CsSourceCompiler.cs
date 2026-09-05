using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Core.Compilation;

namespace CallCenter.Adapters.Compilation
{
    /// <summary>
    /// P4-b C# 编译轨（Source→IL Build 腿）：把一段 C# 源码经仓库既有 Roslyn 编译核心
    /// <see cref="DynamicCompilation"/>（微软 Roslyn 5.3.0，含自动引用解析/Release 配置，非自造编译栈，
    /// SingleImplementation / TH-12）编译为 IL 字节产物。产物形态为 <see cref="byte[]"/>（PE），
    /// 由调用方按 fingerprint 写入 <see cref="Core.Cache.BuildCache"/>（编译缓存多轨制）——本类只做编译，
    /// 不做激活（激活由持久宿主/协调器驱动），与 <c>CallCenter.Mcp.TsScriptCompiler</c> 语义对齐。
    /// <para><b>作用域</b>：与 TS 腿对称的 C# 腿，供「改码 → 新版本生效」的 Source/Build 轨目标端使用；
    /// 返回字节而非 <see cref="System.Reflection.Assembly"/>，避免在编译期即污染宿主进程程序集加载。</para>
    /// </summary>
    public sealed class CsSourceCompiler
    {
        private readonly OptimizationLevel _optimization;
        private readonly IReadOnlyList<MetadataReference>? _extraReferences;

        /// <summary>以给定优化等级与可选额外元数据引用构造（额外引用经 DynamicCompilation 自动解析结果之上追加）。</summary>
        public CsSourceCompiler(OptimizationLevel optimization = OptimizationLevel.Release, IReadOnlyList<MetadataReference>? extraReferences = null)
        {
            _optimization = optimization;
            _extraReferences = extraReferences;
        }

        /// <summary>
        /// 编译一段 C# 源文本为 IL 字节（PE）。失败抛 <see cref="InvalidOperationException"/> 携带全部诊断（含行定位）；
        /// 返回字节可 <see cref="System.Reflection.Assembly.Load(byte[])"/> 或写 <c>BuildCache</c>。
        /// </summary>
        public async Task<byte[]> CompileAsync(string code, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("C# 源为空。", nameof(code));

            ct.ThrowIfCancellationRequested();

            CSharpCompilation compilation;
            try
            {
                // 复用仓库 Roslyn 核心：BuildCompilation 已含自动引用解析（按 using 命名空间匹配已加载程序集），
                // 语法/语义错误在编译期即抛 CompilationException。
                compilation = DynamicCompilation.BuildCompilation(code);
            }
            catch (DynamicCompilation.CompilationException ce)
            {
                throw new InvalidOperationException(ce.ErrorDetails, ce);
            }

            if (_extraReferences is { Count: > 0 })
                compilation = compilation.AddReferences(_extraReferences);

            return await EmitToBytesAsync(compilation).ConfigureAwait(false);
        }

        /// <summary>从文件读取 C# 源并编译为 IL 字节。</summary>
        public async Task<byte[]> CompileFileAsync(string sourcePath, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"C# 源文件未找到: {sourcePath}", sourcePath);

            var code = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
            return await CompileAsync(code, ct).ConfigureAwait(false);
        }

        /// <summary>Emit compilation 到内存字节；失败聚合全部错误诊断（含源码行定位）后抛出。</summary>
        private static async Task<byte[]> EmitToBytesAsync(CSharpCompilation compilation)
        {
            await Task.Yield(); // 纯内存 CPU 密集编译放到线程池，避免阻塞调用线程。

            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);

            if (!emit.Success)
            {
                var sb = new StringBuilder("C# 编译失败：");
                foreach (var d in emit.Diagnostics)
                {
                    if (d.Severity != DiagnosticSeverity.Error) continue;
                    var loc = d.Location.GetLineSpan();
                    sb.Append($"{d.Id} {d.GetMessage()} @{loc.Path}:{loc.StartLinePosition.Line + 1} | ");
                }
                throw new InvalidOperationException(sb.ToString().TrimEnd(' ', '|'));
            }

            ms.Position = 0;
            return ms.ToArray();
        }
    }
}
