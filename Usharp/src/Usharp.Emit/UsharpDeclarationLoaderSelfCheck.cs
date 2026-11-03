using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 装载骨架自检（E-4 反例自动化）：<c>verify</c> 直接调用，零测试工程、零 NuGet。
/// <para>
/// 断言两件事：① <c>emit</c> 回调抛异常且 <c>log:null</c> 时结果仍含 <c>US0017</c>（失败不静默）；
/// ② 空源集 ⇒ <see cref="DeclarationLoadStatus.NotDeclared"/> 且不产诊断（未声明≠失败）。
/// </para>
/// </summary>
public static class UsharpDeclarationLoaderSelfCheck
{
    /// <summary>执行自检；返回问题清单（空 = 通过）。</summary>
    public static IReadOnlyList<string> Run()
    {
        var issues = new List<string>();
        var source = new DeclarationSource("loader-selfcheck", "(memory)", "SelfCheck");

        var thrown = UsharpDeclarationLoader.LoadSources<object>(
            new[] { new UsharpSourceFile("(memory)", string.Empty) },
            source,
            (_, _) => throw new InvalidOperationException("emit-boom"),
            log: null);

        if (thrown.Status != DeclarationLoadStatus.Exception)
            issues.Add($"loader-selfcheck: 期望 Status=Exception，实得 {thrown.Status}");

        if (!thrown.Diagnostics.Any(d => d.Code == UsharpCodes.DeclarationLoadFailure))
            issues.Add("loader-selfcheck: 异常路径缺少 US0017 诊断");

        var empty = UsharpDeclarationLoader.LoadSources<object>(
            Array.Empty<UsharpSourceFile>(),
            source,
            (_, _) => DeclarationEmitOutput<object>.Of(null),
            log: null);

        if (empty.Status != DeclarationLoadStatus.NotDeclared)
            issues.Add($"loader-selfcheck: 空源集期望 NotDeclared，实得 {empty.Status}");

        if (empty.Diagnostics.Count != 0)
            issues.Add($"loader-selfcheck: 空源集不应产诊断（实得 {empty.Diagnostics.Count} 条）");

        return issues;
    }
}