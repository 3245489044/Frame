using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>一次声明面装载的结局（消费方据此决定「激活 / 降级回退」与来源标记）。</summary>
public enum DeclarationLoadStatus
{
    /// <summary>装载成功（<see cref="DeclarationLoadResult{T}.Value"/> 非空）。</summary>
    Loaded,

    /// <summary>未声明（路径未解析 / 文件不存在 / 无源文档）：不激活，不假装通过。</summary>
    NotDeclared,

    /// <summary>闸A 编译失败（诊断已逐条上报）。</summary>
    CompileFailed,

    /// <summary>发射失败（产物为空 / 发射期错误）。</summary>
    EmitFailed,

    /// <summary>装载期异常（已上报为可见化事件）。</summary>
    Exception,
}

/// <summary>
/// 发射回调产物：领域产物（<typeparamref name="T"/>）+ 发射期诊断。
/// <typeparamref name="T"/> 为 null 或诊断含 error ⇒ 判为 <see cref="DeclarationLoadStatus.EmitFailed"/>。
/// </summary>
/// <param name="Value">领域产物（如骨架契约 / 层次 schema）。</param>
/// <param name="Diagnostics">发射期诊断。</param>
public sealed record DeclarationEmitOutput<T>(T? Value, IReadOnlyList<UsharpDiagnostic> Diagnostics)
    where T : class
{
    /// <summary>由产物 + 诊断构筑（无诊断便捷形态）。</summary>
    public static DeclarationEmitOutput<T> Of(T? value, IReadOnlyList<UsharpDiagnostic>? diagnostics = null)
        => new(value, diagnostics ?? Array.Empty<UsharpDiagnostic>());
}

/// <summary>
/// 一次声明面装载的结果：结局 + 路径 + 领域产物 + **编译单元**（供消费方后处理，如转移表登记，
/// 避免二次编译）+ 诊断。
/// </summary>
/// <param name="Status">结局。</param>
/// <param name="Path">已解析路径（未解析时为 null）。</param>
/// <param name="Value">领域产物（未成功时为 null）。</param>
/// <param name="Compilation">编译单元（未编译时为 null）。</param>
/// <param name="Diagnostics">诊断（编译期 + 发射期）。</param>
public sealed record DeclarationLoadResult<T>(
    DeclarationLoadStatus Status,
    string? Path,
    T? Value,
    UsharpCompilation? Compilation,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
    where T : class
{
    /// <summary>是否成功装载（结局为 <see cref="DeclarationLoadStatus.Loaded"/> 且产物非空）。</summary>
    public bool Succeeded => Status == DeclarationLoadStatus.Loaded && Value is not null;

    /// <summary>「未声明」结果（不激活；调用方据此走内置降级或显式不激活）。</summary>
    /// <param name="path">已解析路径（可为 null）。</param>
    public static DeclarationLoadResult<T> NotDeclared(string? path)
        => new(DeclarationLoadStatus.NotDeclared, path, null, null, Array.Empty<UsharpDiagnostic>());
}
