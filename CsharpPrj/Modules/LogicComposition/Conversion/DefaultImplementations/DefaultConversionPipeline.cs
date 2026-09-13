
using System;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.DefaultImplementations
{
    /// <summary>
    /// 默认转换管线：按声明顺序执行阶段，异常统一转诊断。
    /// <see cref="ConversionOptions.CollectAllDiagnostics"/>=true 时「全量失败可见」（继续执行，保留最后可用节点）；
    /// =false 时首个 Error 即短路。任一阶段抛出后**不会**把该阶段产物并入链。
    /// </summary>
    public sealed class DefaultConversionPipeline : IConversionPipeline
    {
        private readonly List<IConversionStage> _stages;

        public DefaultConversionPipeline(IEnumerable<IConversionStage> stages)
        {
            if (stages is null) throw new ArgumentNullException(nameof(stages));
            _stages = stages.ToList();

            for (var i = 0; i < _stages.Count; i++)
            {
                if (_stages[i] is null)
                    throw new ArgumentException($"第 {i} 个转换阶段为 null", nameof(stages));
            }
        }

        public DefaultConversionPipeline(params IConversionStage[] stages)
            : this((IEnumerable<IConversionStage>)(stages ?? Array.Empty<IConversionStage>()))
        {
        }

        public IReadOnlyList<IConversionStage> Stages => _stages;

        public ConversionResult<ConversionNode> Run(ConversionNode input, ConversionContext? context = null)
        {
            if (input is null)
                return ConversionResult<ConversionNode>.Failure("CVT-PIPE-NULL", "管线输入为 null");

            var ctx = context ?? new ConversionContext();
            var current = input;

            for (var i = 0; i < _stages.Count; i++)
            {
                var stage = _stages[i];

                try
                {
                    var next = stage.Execute(current, ctx);
                    if (next is null)
                    {
                        ctx.Diagnostics.Report(ConversionDiagnostic.Error(
                            "CVT-STAGE-NULL",
                            $"阶段 \"{stage.Name}\" 返回 null（已保留上一阶段产物）",
                            ctx.SpanAt(0, 0)));
                    }
                    else
                    {
                        current = next;
                    }
                }
                catch (ConversionStageException ex)
                {
                    ctx.Diagnostics.Report(ConversionDiagnostic.Error(
                        ex.Code,
                        $"阶段 \"{stage.Name}\"：{ex.Message}",
                        ex.Span));
                }
                catch (Exception ex)
                {
                    ctx.Diagnostics.Report(ConversionDiagnostic.Error(
                        "CVT-STAGE-EX",
                        $"阶段 \"{stage.Name}\" 抛出 {ex.GetType().Name}：{ex.Message}",
                        ctx.SpanAt(0, 0)));
                }

                if (!ctx.Options.CollectAllDiagnostics && ctx.Diagnostics.HasErrors) break;
            }

            return ctx.Diagnostics.HasErrors
                ? ConversionResult<ConversionNode>.Failure(ctx.Diagnostics.Diagnostics, current)
                : ConversionResult<ConversionNode>.Success(current, ctx.Diagnostics.Diagnostics);
        }
    }
}
