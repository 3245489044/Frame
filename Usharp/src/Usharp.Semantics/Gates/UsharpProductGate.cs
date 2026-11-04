using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>派生绑定形态（<c>[Derive]</c> 的声明 Id：字母/下划线起，字母数字下划线）。</summary>
    private static readonly Regex DeriveIdPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>血缘字段对分隔符（与 <c>TopologyLineageKeys</c> 同口径：<c>;</c> 项分隔 / <c>-&gt;</c> 字段分隔）。</summary>
    private const char DerivesPairSeparator = ';';

    private const string DerivesFieldSeparator = "->";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var declared = new Dictionary<string, UsharpProductDecl>(StringComparer.Ordinal);
        var deriveIds = new HashSet<string>(StringComparer.Ordinal);

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

            CheckProductShape(product, model, deriveIds, sink);
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
        UsharpProductDecl product, UsharpGateModel model, HashSet<string> deriveIds,
        IConversionDiagnosticSink sink)
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

        // 字段级血缘（derives=）：独立于 derive= 校验（两者可并存，任一缺失不跳过另一项）。
        CheckProductDerives(product, sink);

        // 派生绑定（derive=）：形态须为 [Derive] 的声明 Id；同一 Id 不得被两个产品同时镜像（边端点歧义）。
        if (product.DeriveId is not { Length: > 0 } derive) return;

        if (!DeriveIdPattern.IsMatch(derive))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: derive='{derive}' 形态非法（须 [Derive] 声明 Id：字母/下划线起）",
                product.SourceFile, product.LineNumber));
            return;
        }

        if (!deriveIds.Add(derive))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: derive='{derive}' 已被其他产品镜像（派生 Id 须唯一对应一个产品键）",
                product.SourceFile, product.LineNumber));
        }
    }

    /// <summary>字段级血缘（<c>derives=</c>）：端点上唯一（<c>depends_on</c> 恰 1 项）+ 字段对形态合法。</summary>
    private static void CheckProductDerives(UsharpProductDecl product, IConversionDiagnosticSink sink)
    {
        if (product.Derives is not { Length: > 0 } derives) return;

        if (product.DependsOn.Count != 1)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                $"[Product] {product.ProductId}: derives= 须恰有 1 个 depends_on 上游（现 {product.DependsOn.Count} 个；多上游时血缘端点不可判定）",
                product.SourceFile, product.LineNumber));
            return;
        }

        foreach (var pair in derives.Split(DerivesPairSeparator))
        {
            var entry = pair.Trim();
            if (entry.Length == 0) continue;

            var separator = entry.IndexOf(DerivesFieldSeparator, StringComparison.Ordinal);
            if (separator <= 0 || separator + DerivesFieldSeparator.Length >= entry.Length)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                    $"[Product] {product.ProductId}: derives 字段对 '{entry}' 缺 {DerivesFieldSeparator}",
                    product.SourceFile, product.LineNumber));
                continue;
            }

            var from = entry[..separator].Trim();
            var to = entry[(separator + DerivesFieldSeparator.Length)..].Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ProductFieldInvalid,
                    $"[Product] {product.ProductId}: derives 字段对 '{entry}' 含空字段",
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