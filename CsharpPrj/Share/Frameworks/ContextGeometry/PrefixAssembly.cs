namespace Share.ContextGeometry;

/// <summary>
/// 前缀装配输入（唯一装配口径）：段集合 + 工具面暴露 + 共享块。
/// <para>共享块先于专属段发射（公共前缀在前，专属尾部在后）；工具面按声明裁剪。</para>
/// </summary>
public sealed record PrefixAssemblyInput(
    string Scope,
    IReadOnlyList<PrefixSegment>? Segments = null,
    bool ToolsExposed = true,
    IReadOnlyList<PrefixSegment>? SharedBlocks = null);

/// <summary>前缀装配计划（纯函数产物）：段序 + 摘要 + 规范字节引用 + 诊断。</summary>
public sealed record PrefixAssemblyPlan(
    string Scope,
    IReadOnlyList<PrefixSegment> Segments,
    bool ToolsExposed,
    string Digest,
    string? CanonicalRef,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>工具面段数（结构断言用；0 = 未注入任何工具 schema）。</summary>
    public int ToolCount => PrefixAssembly.ToolCount(this);
}

/// <summary>
/// 前缀装配（纯函数；排序/去重/摘要一律委托 <see cref="PrefixComposer"/>，禁第二套实现）。
/// <para>纪律：<see cref="PrefixAssemblyInput.ToolsExposed"/>=false ⇒ 段集合内不得出现工具面段。</para>
/// </summary>
public static class PrefixAssembly
{
    /// <summary>工具面段键（唯一命名；裁剪判据只认此键）。</summary>
    public const string ToolsSegmentKey = "prefix.tools";

    /// <summary>装配前缀（同输入两次 ⇒ 段序与摘要逐字相等）。</summary>
    public static PrefixAssemblyPlan Plan(PrefixAssemblyInput? input, ISharedBlockStore? blocks = null)
    {
        var diagnostics = new List<string>();
        if (input is null)
        {
            diagnostics.Add(GeometryDiagnostics.PrefixAbsent);
            return new PrefixAssemblyPlan(string.Empty, Array.Empty<PrefixSegment>(), false, string.Empty, null, diagnostics);
        }

        var segments = new List<PrefixSegment>();

        // 共享块与专属段走同一工具面裁剪（ContextGeometry.md §4.10，原 :46-47 AddRange 绕过 ToolsExposed=false：
        // 声明"不暴露工具"却仍可经 SharedBlocks 注入 prefix.tools 段，工具面过滤形同虚设）。
        foreach (var segment in input.SharedBlocks ?? Array.Empty<PrefixSegment>())
        {
            if (segment is null) continue;
            if (!input.ToolsExposed && IsToolsSegment(segment.Key))
            {
                diagnostics.Add($"{GeometryDiagnostics.ToolFaceHidden}:{segment.Key}");
                continue;
            }
            segments.Add(segment);
        }

        foreach (var segment in input.Segments ?? Array.Empty<PrefixSegment>())
        {
            if (segment is null) continue;
            if (!input.ToolsExposed && IsToolsSegment(segment.Key))
            {
                diagnostics.Add($"{GeometryDiagnostics.ToolFaceHidden}:{segment.Key}");
                continue;
            }
            segments.Add(segment);
        }

        var composed = PrefixComposer.Compose(segments, blocks);
        diagnostics.AddRange(composed.Diagnostics);
        return new PrefixAssemblyPlan(
            input.Scope, composed.Segments, input.ToolsExposed, composed.Digest, composed.CanonicalRef, diagnostics);
    }

    /// <summary>工具面段计数（结构事实；不读语义）。</summary>
    public static int ToolCount(PrefixAssemblyPlan? plan)
        => plan is null ? 0 : plan.Segments.Count(s => IsToolsSegment(s.Key));

    /// <summary>是否工具面段（键前缀判定；口径唯一）。</summary>
    public static bool IsToolsSegment(string? key)
        => !string.IsNullOrWhiteSpace(key)
           && key.StartsWith(ToolsSegmentKey, StringComparison.Ordinal);
}
