using UniversalWorkflowFramework.Reference.Models;
using Usharp.Semantics;

namespace Usharp.Adapters.UniversalReference;

/// <summary>
/// 边类型映射表（Usharp 任意边名 → UR 封闭枚举 <see cref="ReferenceType"/> 6 值）。
/// <para>硬约束：UR 的 <c>ReferenceType</c> 只有 6 值，而 Usharp 的 <c>[Edge]</c> 是**任意名** ——
/// 故映射表必须显式声明并**进闸A**（<see cref="Validate"/>）：</para>
/// <list type="bullet">
/// <item><c>ur=</c> 显式映射：非法值 → <see cref="UsharpCodes"/> 域外的 US4003（Error）。</item>
/// <item>边名本身就是 6 值之一（含内置边表）→ 直接采用。</item>
/// <item>其余（无 <c>ur=</c> 且不是 6 值）→ 塌缩为 <c>Other</c> 并报 US4002（Warning，禁止静默塌缩）。</item>
/// </list>
/// <para>注意：<c>IResourceReferenceStore</c> 的 <c>string refType</c> 通道**不塌缩**（保留原名 / <c>ur=</c> 值）；
/// US4002 只针对走 UR 枚举通道（<c>IUniversalReferenceManager</c>）的场景。</para>
/// </summary>
public sealed class UsharpEdgeTypeMap
{
    /// <summary>显式映射字段名（<c>[Edge] E: ... | ur=Dependency</c>）。</summary>
    public const string FieldUr = "ur";

    /// <summary>内置边表（Usharp 内建边 → UR 词汇表；<c>Over</c>/<c>RowRef</c> 不落图故无映射）。</summary>
    public static readonly IReadOnlyDictionary<string, ReferenceType> BuiltIn =
        new Dictionary<string, ReferenceType>(StringComparer.Ordinal)
        {
            [UsharpGraphBuilder.StepRef] = ReferenceType.Includes,
            [UsharpGraphBuilder.Invokes] = ReferenceType.Uses,
            [UsharpGraphBuilder.CrRef] = ReferenceType.Requires,
            [UsharpGraphBuilder.RefersTo] = ReferenceType.Other,
        };

    /// <summary>UR 词汇表 6 值（唯一合法映射目标）。</summary>
    public static readonly IReadOnlyList<string> Vocabulary = Enum.GetNames<ReferenceType>();

    /// <summary>
    /// 解析一个边名（可选 <paramref name="ur"/> 显式映射）→ UR 枚举值；
    /// 未映射时产出 US4002 告警（返回 <c>Other</c>），非法 ur 值时产出 US4003 错误。
    /// </summary>
    public ReferenceType Resolve(string edgeName, string? ur, out UsharpDiagnostic? diagnostic, string file = "", int line = 0)
    {
        diagnostic = null;

        if (!string.IsNullOrWhiteSpace(ur))
        {
            if (Enum.TryParse<ReferenceType>(ur.Trim(), ignoreCase: true, out var declared)) return declared;

            diagnostic = UsharpDiagnostic.Error(UsharpCodes.EdgeTypeUnknown,
                $"边 '{edgeName}' 的 ur='{ur}' 不是合法 ReferenceType（合法值：{string.Join("/", Vocabulary)}）", file, line);
            return ReferenceType.Other;
        }

        if (Enum.TryParse<ReferenceType>(edgeName, ignoreCase: true, out var byName)) return byName;
        if (BuiltIn.TryGetValue(edgeName, out var builtIn)) return builtIn;

        diagnostic = UsharpDiagnostic.Warn(UsharpCodes.EdgeTypeUnmapped,
            $"边类型 '{edgeName}' 未映射到 UniversalReference 词汇表（将塌缩为 Other）—— 请显式声明 ur=Dependency|Includes|Uses|Extends|Requires|Other",
            file, line);
        return ReferenceType.Other;
    }

    /// <summary>
    /// 映射表闸A：扫描全部 <c>[Edge]</c> 声明 —— <c>ur=</c> 非法 → US4003（Error）；
    /// 无 <c>ur=</c> 且边名不是 6 值 → US4002（Warning，显式可见化塌缩）。
    /// </summary>
    public IReadOnlyList<UsharpDiagnostic> Validate(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        foreach (var doc in compilation.Documents)
            foreach (var decl in doc.Declarations)
            {
                if (decl.Kind != Plate.DslMethod.DslSymbolKind.Edge || string.IsNullOrWhiteSpace(decl.Name)) continue;

                Resolve(decl.Name, decl.Field(FieldUr), out var diagnostic, decl.SourceFile, decl.LineNumber);
                if (diagnostic is not null) diagnostics.Add(diagnostic);
            }
        return diagnostics;
    }
}
