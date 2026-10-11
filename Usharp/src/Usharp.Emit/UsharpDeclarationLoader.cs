using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 声明面装载骨架（**与 Agent 领域无关**的可复用管线）：
/// <c>路径 → 源文档 → CompileSources（闸A 编译期拦截）→ 领域发射器 → 产物</c>，
/// 诊断逐条上报（不静默丢弃）、失败一律「不激活」而非抛出（除异常路径转可见化）。
/// <para>
/// 领域分歧点全部由消费方给定：路径解析（配置面）、流号（<c>[Flow]</c> 映射单源）、发射器（产什么）、
/// 日志键（键名逐字保持）。
/// </para>
/// <para>
/// 口径：<see cref="DeclarationLoadStatus.NotDeclared"/> 不产诊断、不产日志以外的副作用 ——
/// 「未声明 ⇒ 零行为变化」由消费方按其降级口径实现（内置回退 / 显式不激活 + 可见化）。
/// </para>
/// </summary>
public static class UsharpDeclarationLoader
{
    /// <summary>
    /// 装载一次声明面（就绪语义：编译/发射任一失败 ⇒ 不产产物；异常 ⇒ 可见化后判失败）。
    /// </summary>
    /// <typeparam name="T">领域产物类型（骨架契约 / 层次 schema 等）。</typeparam>
    /// <param name="source">声明面输入（逻辑名 + 路径 + 流号）。</param>
    /// <param name="emit">领域发射回调（<c>(compilation, flowId) → 产物 + 诊断</c>）。</param>
    /// <param name="log">告警端口（null ⇒ 仅返回结局，不落日志）。</param>
    public static DeclarationLoadResult<T> Load<T>(
        DeclarationSource source,
        Func<UsharpCompilation, string?, DeclarationEmitOutput<T>> emit,
        IDeclarationLogSink? log = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(emit);

        if (!source.IsResolvable || !File.Exists(source.Path))
        {
            log?.Report(DeclarationLogEvent.NotDeclared, source.BaseName, source.FlowId, source.Path, null);
            return DeclarationLoadResult<T>.NotDeclared(source.Path);
        }

        return LoadSources(UsharpSourceLoader.FromPath(source.Path!), source, emit, log);
    }

    /// <summary>
    /// 由**已装载声明条目**编译/发射（声明库入口，**盘/库同形**）：条目来源无关
    /// （文件库 / 数据库库 / 内存），本方法只负责「源集 → 编译 → 发射 → 诊断归因」。
    /// <para>空集 ⇒ <see cref="DeclarationLoadStatus.NotDeclared"/>（不假装通过）。</para>
    /// </summary>
    public static DeclarationLoadResult<T> LoadFromEntries<T>(
        IReadOnlyList<DeclarationEntry> entries,
        DeclarationSource source,
        Func<UsharpCompilation, string?, DeclarationEmitOutput<T>> emit,
        IDeclarationLogSink? log = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(emit);

        return LoadSources(UsharpSourceLoader.FromEntries(entries), source, emit, log);
    }

    /// <summary>
    /// 由**已装载源集**编译/发射（统一尾段：源集 → 编译 → 发射 → 诊断归因）。
    /// 空集 ⇒ <see cref="DeclarationLoadStatus.NotDeclared"/>；编译/发射失败 ⇒ 对应状态 + 诊断逐条上报。
    /// </summary>
    public static DeclarationLoadResult<T> LoadSources<T>(
        IReadOnlyList<UsharpSourceFile> sources,
        DeclarationSource source,
        Func<UsharpCompilation, string?, DeclarationEmitOutput<T>> emit,
        IDeclarationLogSink? log = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(emit);

        if (sources.Count == 0)
        {
            // 无可装载源文档：等价「未声明」（不产诊断，交由调用方降级口径）。
            log?.Report(DeclarationLogEvent.NotDeclared, source.BaseName, source.FlowId, source.Path, null);
            return DeclarationLoadResult<T>.NotDeclared(source.Path);
        }

        try
        {
            var compilation = UsharpCompilation.CompileSources(sources);
            if (compilation.HasErrors)
            {
                var errors = compilation.Errors;
                log?.Report(DeclarationLogEvent.CompileFailed, source.BaseName, source.FlowId, source.Path, errors.Count);
                foreach (var diag in errors)
                    log?.Report(DeclarationLogEvent.CompileDiagnostic, source.BaseName, source.FlowId, source.Path, diag);
                return new DeclarationLoadResult<T>(
                    DeclarationLoadStatus.CompileFailed, source.Path, null, compilation, errors);
            }

            var output = emit(compilation, source.FlowId);
            if (output.Value is null || UsharpDiagnostics.HasError(output.Diagnostics))
            {
                log?.Report(DeclarationLogEvent.EmitFailed, source.BaseName, source.FlowId, source.Path, output.Diagnostics.Count);
                foreach (var diag in output.Diagnostics)
                    log?.Report(DeclarationLogEvent.EmitDiagnostic, source.BaseName, source.FlowId, source.Path, diag);
                return new DeclarationLoadResult<T>(
                    DeclarationLoadStatus.EmitFailed, source.Path, null, compilation, output.Diagnostics);
            }

            log?.Report(DeclarationLogEvent.Loaded, source.BaseName, source.FlowId, source.Path, output.Value);
            return new DeclarationLoadResult<T>(
                DeclarationLoadStatus.Loaded, source.Path, output.Value, compilation, output.Diagnostics);
        }
        catch (Exception ex)
        {
            log?.Report(DeclarationLogEvent.Exception, source.BaseName, source.FlowId, source.Path, ex);
            return new DeclarationLoadResult<T>(
                DeclarationLoadStatus.Exception, source.Path, null, null, Array.Empty<UsharpDiagnostic>());
        }
    }

    /// <summary>诊断是否为 error（判据单源 = <see cref="UsharpDiagnostics"/>；本方法仅作既有调用面转发）。</summary>
    /// <param name="diagnostic">诊断。</param>
    public static bool IsError(UsharpDiagnostic diagnostic)
        => UsharpDiagnostics.IsError(diagnostic);
}
