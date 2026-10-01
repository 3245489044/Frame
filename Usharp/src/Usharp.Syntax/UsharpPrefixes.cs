
using System.Runtime.CompilerServices;
using Plate.DslMethod;
using Plate.DslMethod.Parsing;

namespace Usharp.Syntax;

/// <summary>
/// Usharp 前缀作用域（领域包注册）：五前缀 <c>[Space]</c>/<c>[Matrix]</c>/<c>[Flow]</c>/<c>[Ref]</c>/<c>[Obs]</c>。
/// <para>
/// 前缀由本包自持（<b>不再写入框架核心表</b> <c>DslConstants.Prefixes</c>），并注册到
/// <see cref="DslPrefixRegistry"/>（id = <see cref="PackId"/>），使 Usharp 语言面与其它领域包互不可见。
/// 解析入口 <see cref="UsharpParser"/> 一律以 <see cref="Scope"/> 为作用域。
/// </para>
/// <para>
/// 作用域 = <b>框架核心前缀 ∪ 本包五前缀</b>：Usharp 文档仍可使用核心词汇（如 UniversalReference 的
/// <c>[Node]</c>/<c>[Edge]</c>）；「互不可见」针对的是各<b>领域包</b>之间的私有前缀。
/// </para>
/// </summary>
public static class UsharpPrefixes
{
    /// <summary>包标识（<see cref="DslPrefixRegistry"/> 注册键）。</summary>
    public const string PackId = "usharp";

    /// <summary>本包私有前缀（长度降序由 <see cref="DslPrefixSet"/> 内部保证）。</summary>
    private static readonly DslPrefixDef[] OwnPrefixes =
    {
        new(UsharpLanguage.SpacePrefix,  DslSymbolKind.Space,  "Space"),
        new(UsharpLanguage.MatrixPrefix, DslSymbolKind.Matrix, "Matrix"),
        new(UsharpLanguage.FlowPrefix,   DslSymbolKind.Flow,   "Flow"),
        new(UsharpLanguage.RefPrefix,    DslSymbolKind.Ref,    "Ref"),
        new(UsharpLanguage.ObsPrefix,    DslSymbolKind.Obs,    "Obs"),
        // R3 控制流语言单元：分支 / 多路分派 / 调用（声明式控制流）
        new(UsharpLanguage.BranchPrefix, DslSymbolKind.Branch, "Branch"),
        new(UsharpLanguage.SwitchPrefix, DslSymbolKind.Switch, "Switch"),
        new(UsharpLanguage.CallPrefix,   DslSymbolKind.Call,   "Call"),
    };

    /// <summary>Usharp 前缀作用域 = 框架核心前缀 ∪ 本包五前缀。</summary>
    public static readonly DslPrefixSet Scope =
        new DslPrefixSet(PackId, DslConstants.Prefixes.Concat(OwnPrefixes));

    /// <summary>把本包作用域登记进注册中心（幂等；本程序集加载时自动执行）。</summary>
    [ModuleInitializer]
    internal static void Register() => DslPrefixRegistry.Register(Scope);
}
