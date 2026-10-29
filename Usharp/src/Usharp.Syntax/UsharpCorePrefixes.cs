using Plate.DslMethod;

namespace Usharp.Syntax;

/// <summary>
/// Usharp **核心结构语言**前缀（13 条）：坐标空间 / 矩阵 / 流 / 引用 / 观测 / 域定义 / 聚拢，
/// 以及声明式控制流（分支 / 多路分派 / 调用 / 区域 / 受控边 / 受护作用域）。
/// <para>
/// 归位判据：这些前缀的语义属「结构语言」（任何领域包装载与否都成立）；领域前缀（表 / 键空间 / LLM 等）
/// 由领域包自持（见 <see cref="UsharpPrefixes.DomainPrefixes"/>，B2 起随包下沉）。
/// </para>
/// </summary>
public static class UsharpCorePrefixes
{
    /// <summary>核心结构前缀定义（声明序；最长优先匹配由 <see cref="Plate.DslMethod.Parsing.DslPrefixSet"/> 内部保证）。</summary>
    public static readonly IReadOnlyList<DslPrefixDef> Definitions = new DslPrefixDef[]
    {
        new(UsharpLanguage.SpacePrefix,  DslSymbolKind.Space,  "Space"),
        new(UsharpLanguage.MatrixPrefix, DslSymbolKind.Matrix, "Matrix"),
        new(UsharpLanguage.FlowPrefix,   DslSymbolKind.Flow,   "Flow"),
        new(UsharpLanguage.RefPrefix,    DslSymbolKind.Ref,    "Ref"),
        new(UsharpLanguage.ObsPrefix,    DslSymbolKind.Obs,    "Obs"),
        // 控制流语言单元：分支 / 多路分派 / 调用（声明式控制流）
        new(UsharpLanguage.BranchPrefix, DslSymbolKind.Branch, "Branch"),
        new(UsharpLanguage.SwitchPrefix, DslSymbolKind.Switch, "Switch"),
        new(UsharpLanguage.CallPrefix,   DslSymbolKind.Call,   "Call"),
        // 区域语言单元：区域声明（父层单节点 + 子计划迭代计划）
        new(UsharpLanguage.RegionPrefix, DslSymbolKind.Region, "Region"),
        // 容错/门控语言单元：受控边（When）/ 受护作用域（Guard）
        new(UsharpLanguage.WhenPrefix, DslSymbolKind.When, "When"),
        new(UsharpLanguage.GuardPrefix, DslSymbolKind.Guard, "Guard"),
        // 域定义语言单元：节点域定义面（[Domain]）
        new(UsharpLanguage.DomainPrefix, DslSymbolKind.Domain, "Domain"),
        // 聚拢声明语言单元：收集端口（[Gather]；U2）
        new(UsharpLanguage.GatherPrefix, DslSymbolKind.Gather, "Gather"),
    };
}
