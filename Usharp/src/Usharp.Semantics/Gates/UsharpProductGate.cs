using System;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：<c>[Product]</c> / <c>[Face]</c> 结构闭包（产品键唯一 / 层已声明 / 字段契约非空 /
/// 幂等键非易变 / 面引用产品存在 / 面字段 ⊆ 产品出字段）。
/// <para>判据载体 = 声明面结构事实（键集合 / 字段集合 / 层表）；不含执行者叙述。</para>
/// </summary>
internal sealed class ProductGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.product";

    /// <summary>幂等键禁用词（易变分量：时间 / 序号 / 随机）；命中即判非结构事实。</summary>
    private static readonly string[] VolatileTokens =
        { "now", "utc", "time", "timestamp", "seq", "random", "uuid", "guid" };

    private static readonly HashSet<string> AcceptsKeys = new(StringComparer.Ordinal) { "owner", "channel" };

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var declared = new Dictionary<string, UsharpProductDecl>(StringComparer.Ordinal);

        foreach (var product in model.Products)
        {
            foreach (var line in product.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductLineUnparsed,
                    $"[Product] {product.ProductId}: 无法解析的续行 '{line}'",
                    product.SourceFile, product.LineNumber));

            if (!declared.TryAdd(product.ProductId, product))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductDuplicate,
                    $"[Product] {product.ProductId}: 重复声明",
                    product.SourceFile, product.LineNumber));
            }

            CheckProductShape(product, model, sink);
        }

        var faces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var face in model.Faces)
        {
            foreach (var line in face.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductLineUnparsed,
                    $"[Face] {face.FaceId}: 无法解析的续行 '{line}'",
                    face.SourceFile, face.LineNumber));

            if (!faces.Add(face.FaceId))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.FaceDuplicate,
                    $"[Face] {face.FaceId}: 重复声明",
                    face.SourceFile, face.LineNumber));
            }

            CheckFaceShape(face, declared, sink);
        }
    }

    private static void CheckProductShape(
        UsharpProductDecl product, UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        if (string.IsNullOrWhiteSpace(product.LevelId))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: 缺 level=（产品必须归属已声明层）",
                product.SourceFile, product.LineNumber));
        }
        else if (!model.Spaces.TryGet(product.LevelId, out _))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductLevelUndeclared,
                $"[Product] {product.ProductId}: level='{product.LevelId}' 未声明为 [Space]",
                product.SourceFile, product.LineNumber));
        }

        if (string.IsNullOrWhiteSpace(product.Role))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: 缺 role=",
                product.SourceFile, product.LineNumber));
        }

        if (product.OutFields.Count == 0)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: out= 为空（对外承诺字段面不得空）",
                product.SourceFile, product.LineNumber));
        }

        if (string.IsNullOrWhiteSpace(product.IdempotencyKey))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: 缺 idempotency=（幂等键为结构事实）",
                product.SourceFile, product.LineNumber));
            return;
        }

        var key = product.IdempotencyKey;
        var volatileHit = VolatileTokens.FirstOrDefault(t => key.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (volatileHit is not null)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: idempotency='{key}' 含易变分量 '{volatileHit}'（禁时间/序号/随机）",
                product.SourceFile, product.LineNumber));
        }

        foreach (var accept in product.Accepts)
        {
            var colon = accept.IndexOf(':');
            var name = colon < 0 ? accept : accept[..colon];
            if (colon <= 0 || colon == accept.Length - 1 || !AcceptsKeys.Contains(name.Trim()))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductAcceptsInvalid,
                    $"[Product] {product.ProductId}: accepts 项 '{accept}' 非法（须 owner:<值> / channel:<值>）",
                    product.SourceFile, product.LineNumber));
            }
        }
    }

    private static void CheckFaceShape(
        UsharpFaceDecl face,
        IReadOnlyDictionary<string, UsharpProductDecl> products,
        IConversionDiagnosticSink sink)
    {
        if (string.IsNullOrWhiteSpace(face.ProductId) || !products.TryGetValue(face.ProductId, out var product))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.FaceProductUndeclared,
                $"[Face] {face.FaceId}: product='{face.ProductId}' 未声明",
                face.SourceFile, face.LineNumber));
            return;
        }

        if (!face.FieldsDeclared) return;

        if (face.Fields.Count == 0)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.FaceFieldsEmpty,
                $"[Face] {face.FaceId}: 声明了 fields= 但解析后为空",
                face.SourceFile, face.LineNumber));
            return;
        }

        var outFields = new HashSet<string>(product.OutFields, StringComparer.Ordinal);
        foreach (var field in face.Fields)
        {
            if (outFields.Contains(field)) continue;

            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.FaceFieldNotSubset,
                $"[Face] {face.FaceId}: fields= 引用 '{field}' 不在产品 '{product.ProductId}' 的 out= 集合",
                face.SourceFile, face.LineNumber));
        }
    }
}