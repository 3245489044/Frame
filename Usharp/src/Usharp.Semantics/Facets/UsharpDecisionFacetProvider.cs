using System;
using System.Collections.Generic;
using Plate.DslMethod;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// <c>[Matrix]</c> 判据读法的**单一投影**（E5）：矩阵续行 → <see cref="UsharpDecisionDecl"/>（按 MatrixId 索引）。
/// <para>
/// 闸A（<c>DecisionGateRule</c>）与装配（<c>UsharpDecisionMatrixEmitter</c>）读取同一份投影，
/// 禁各自再调 <c>UsharpDecisionDecl.From</c> —— 否则两处解析口径可能漂移，形成第二真源。
/// </para>
/// </summary>
internal sealed class DecisionFacetProvider : IUsharpFacetProvider
{
    public Type FacetType => typeof(IReadOnlyDictionary<string, UsharpDecisionDecl>);

    public IReadOnlyList<DslSymbolKind> Kinds => new[] { DslSymbolKind.Matrix };

    public object Project(UsharpFacetContext ctx)
    {
        var index = UsharpDeclarationIdIndex<UsharpMatrixDecl>.Build(
            ctx.Documents.SelectMany(UsharpSyntaxProjector.Matrices),
            matrix => matrix.MatrixId,
            matrix => UsharpSourceRef.At(matrix.SourceFile, matrix.LineNumber));

        // winner = FirstWins（重复本身由 DeclarationIdentityGateRule 报 US0014，产物口径不再各处各写）。
        var map = new Dictionary<string, UsharpDecisionDecl>(StringComparer.Ordinal);
        foreach (var matrix in index.All)
            if (!map.ContainsKey(matrix.MatrixId))
                map[matrix.MatrixId] = UsharpDecisionDecl.From(matrix);
        return map;
    }
}
