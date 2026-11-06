using Plate.DslMethod.Model;

namespace Usharp.Compute;

/// <summary>
/// 文件级分类判据（R0）。判据只有一个：文件物化后是否出现 <c>[Compute]</c> 声明。
/// <para>
/// 反例（会被误判为纯声明、因而需要额外闸门兜住的写法）：
/// 1. <c>[Compute]</c> 写在 fenced 块内：解析器按模板内容吸收，不是声明；分类为纯声明，且也不会被发射。
/// 2. <c>[Compute]</c> 写在前一个声明的缩进续行里（行首有空格）：解析器把它当续行，不是声明；
///    分类会漏判——故 ComputeBoundaryScanner 对"缩进的 Compute 标记"直接报 US8002，使它无法静默成为纯声明。
/// 3. <c>[compute]</c>/<c>[COMPUTE]</c>/全角括号：不是已注册前缀，由既有 US0013 兜底（不会静默通过）。
/// </para>
/// <para>
/// 声明头在语法上无法容纳计算：头的全部值按字段语法逐项校验（kind/类型/枚举/正整数），
/// 并且任何值一旦出现 <c>+ - * / ( ) = ;</c> 立即 US8001；语句只存在于声明后的缩进续行通道。
/// </para>
/// </summary>
public static class UsharpComputeClassifier
{
    public const string ComputePrefix = "[Compute]";

    /// <summary>行为声明种类（用于 isolation 判据）。</summary>
    public static readonly IReadOnlyList<DslSymbolKind> BehaviorKinds = new[]
    {
        DslSymbolKind.Flow,
        DslSymbolKind.Matrix,
        DslSymbolKind.Node,
        DslSymbolKind.Branch,
        DslSymbolKind.Switch,
        DslSymbolKind.Call,
        DslSymbolKind.Region,
        DslSymbolKind.When,
        DslSymbolKind.Guard,
        DslSymbolKind.Command,
        DslSymbolKind.ReadModel
    };

    public static bool IsComputeUnit(DslDeclaration declaration)
        => string.Equals(declaration.Prefix, ComputePrefix, StringComparison.Ordinal);

    public static IReadOnlyList<DslDeclaration> ComputeUnits(DslDocument document)
        => document.Declarations.Where(IsComputeUnit).ToList();

    public static bool HasComputation(DslDocument document)
        => document.Declarations.Any(IsComputeUnit);

    public static bool HasComputation(IEnumerable<DslDocument> documents)
        => documents.Any(HasComputation);

    public static bool IsPureDeclaration(DslDocument document) => !HasComputation(document);

    public static int BehaviorUnits(DslDocument document)
        => document.Declarations.Count(d => BehaviorKinds.Contains(d.Kind));

    public static ComputationPurity Classify(IEnumerable<DslDocument> documents)
    {
        var hasCompute = false;
        var hasMixed = false;
        foreach (var document in documents)
        {
            var compute = HasComputation(document);
            if (!compute) continue;
            hasCompute = true;
            if (BehaviorUnits(document) > 0) hasMixed = true;
        }

        if (!hasCompute) return ComputationPurity.PureDeclaration;
        return hasMixed ? ComputationPurity.ComputationMixed : ComputationPurity.ComputationIsolated;
    }

    public static string PurityLabel(IEnumerable<DslDocument> documents)
        => Classify(documents) switch
        {
            ComputationPurity.PureDeclaration => "pure-declaration",
            ComputationPurity.ComputationIsolated => "computation-isolated",
            ComputationPurity.ComputationMixed => "computation-mixed",
            _ => "unknown"
        };
}

public enum ComputationPurity
{
    PureDeclaration,
    ComputationIsolated,
    ComputationMixed
}
