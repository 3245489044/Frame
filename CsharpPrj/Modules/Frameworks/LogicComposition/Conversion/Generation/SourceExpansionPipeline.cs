using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Generation;

/// <summary>
/// 源展开管线（**唯一编排实现**）：<c>解析 → 格式级诊断 → 语义级校验 → 确定性发射</c>。
/// <para>
/// 多宿主（编译期校验 / 构建期写盘 / 重生成一致性守卫）共用同一管线，保证判定完全同源：
/// 不会出现「编译期过、写盘失败」的口径漂移。
/// </para>
/// </summary>
/// <typeparam name="TModel">领域解析模型。</typeparam>
public sealed class SourceExpansionPipeline<TModel>
{
    private readonly Func<string, string, ISourceFrontEnd<TModel>> _frontEndFactory;
    private readonly Func<TModel, string?, ConversionResult<string>> _emit;

    /// <summary>构造管线。</summary>
    /// <param name="frontEndFactory">(诊断路径, 源文本) → 源前端。</param>
    /// <param name="emit">(模型, 头注释) → 确定性产物文本。</param>
    public SourceExpansionPipeline(
        Func<string, string, ISourceFrontEnd<TModel>> frontEndFactory,
        Func<TModel, string?, ConversionResult<string>> emit)
    {
        _frontEndFactory = frontEndFactory ?? throw new ArgumentNullException(nameof(frontEndFactory));
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
    }

    /// <summary>解析 + 校验（不发射）。</summary>
    /// <param name="diagnosticPath">诊断定位所用路径（绝对路径，写入 <c>SourceSpan.File</c>）。</param>
    /// <param name="text">源文本。</param>
    /// <param name="model">out：解析所得模型（即使有诊断也返回，供调用方做局部判定）。</param>
    public IReadOnlyList<ConversionDiagnostic> Check(string diagnosticPath, string text, out TModel model)
    {
        if (diagnosticPath is null) throw new ArgumentNullException(nameof(diagnosticPath));

        var frontEnd = _frontEndFactory(diagnosticPath, text);
        model = frontEnd.Parse(diagnosticPath, text);

        var diagnostics = new List<ConversionDiagnostic>(frontEnd.Diagnostics);
        diagnostics.AddRange(frontEnd.Validate(model));
        return diagnostics;
    }

    /// <summary>展开为确定性产物文本（含头注释）。</summary>
    /// <param name="diagnosticPath">诊断定位所用路径（绝对路径）。</param>
    /// <param name="sourceRelativePath">源相对工程目录的路径（仅用于头注释，保证跨机确定性）。</param>
    /// <param name="text">源文本。</param>
    /// <param name="headerComment">头注释生成器（null = 不写头注释）。</param>
    public ExpansionOutcome Expand(
        string diagnosticPath,
        string sourceRelativePath,
        string text,
        Func<string, string?>? headerComment = null)
    {
        var diagnostics = Check(diagnosticPath, text, out var model);
        if (HasError(diagnostics)) return new ExpansionOutcome(diagnostics, null);

        var emitted = _emit(model, headerComment?.Invoke(sourceRelativePath));
        if (emitted.Diagnostics.Count > 0)
        {
            var all = new List<ConversionDiagnostic>(diagnostics);
            all.AddRange(emitted.Diagnostics);
            diagnostics = all;
        }

        return new ExpansionOutcome(diagnostics, emitted.IsSuccess ? emitted.Artifact : null);
    }

    private static bool HasError(IReadOnlyList<ConversionDiagnostic> diagnostics)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == ConversionSeverity.Error) return true;
        }

        return false;
    }
}
