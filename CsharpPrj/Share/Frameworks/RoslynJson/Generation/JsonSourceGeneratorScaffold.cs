
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Share.RoslynJson.Generation;

/// <summary>
/// 增量源生成器脚手架：收敛 <c>AdditionalTextsProvider → Collect → RegisterSourceOutput</c> 的固定接线，
/// 并强制「路径序 + 异常兜底」两条纪律，派生生成器只实现「哪些文件算输入」与「如何产出」。
/// </summary>
public abstract class JsonSourceGeneratorScaffold : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(IsSource)
            .Collect();

        context.RegisterSourceOutput(inputs, (productionContext, texts) =>
        {
            try
            {
                Execute(productionContext, GeneratorInputOrdering.OrderByPath(texts));
            }
            catch (Exception ex)
            {
                // 生成器异常必须转诊断：否则编译器只报笼统的分析器崩溃
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.RoslynJsonDiagnostics.GeneratorFailure,
                    Location.None,
                    ex.GetType().Name,
                    ex.Message));
            }
        });
    }

    /// <summary>判定该附加文件是否为本生成器的输入（如扩展名匹配）。</summary>
    protected abstract bool IsSource(AdditionalText text);

    /// <summary>执行生成（输入已按路径序排序）。</summary>
    protected abstract void Execute(SourceProductionContext context, IReadOnlyList<AdditionalText> inputs);
}
