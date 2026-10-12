namespace Usharp.Semantics;

/// <summary>
/// 引用边（规则 R1「引用即边」）：一切引用都落为引用图的一条边，矩阵只是图上的属性
/// ——故反向引用/影响分析/环检测由 <c>Share.Frameworks.UniversalReference</c> 图算法免费给出。
/// </summary>
/// <param name="Type">边类型（<c>RefersTo</c>/<c>Over</c>/<c>StepRef</c>/<c>Invokes</c>/<c>CrRef</c>，或 [Edge]/intent 声明值）。</param>
/// <param name="Source">源符号（所在声明名）。</param>
/// <param name="Target">目标符号（坐标键 / 空间号 / 子流号 / [CR] 原文）。</param>
/// <param name="SpaceId">该边引用的空间归属（无空间归属时为 null）。</param>
/// <param name="Key">该边引用的坐标键（无坐标时为 null）。</param>
/// <param name="SourceFile">声明来源文件。</param>
/// <param name="LineNumber">声明行号（1-based）。</param>
public sealed record UsharpEdge(
    string Type,
    string Source,
    string Target,
    string? SpaceId,
    string? Key,
    string SourceFile,
    int LineNumber)
{
    public override string ToString()
        => SpaceId is null ? $"{Source} -[{Type}]-> {Target}" : $"{Source} -[{Type}]-> {SpaceId}:{Target}";
}
