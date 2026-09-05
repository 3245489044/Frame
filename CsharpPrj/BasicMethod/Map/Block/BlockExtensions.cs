
#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{
    public static class BlockExtensions
    {

        public static IBlock ToBlock(
            this Func<BlockContext, CancellationToken, Task<BlockResult>> func,
            string name,
            string? description = null)
        {
            return new DelegateBlock(name, func, description);
        }

        public static IBlock ToBlock(
            this Func<BlockContext, BlockResult> func,
            string name,
            string? description = null)
        {
            return new DelegateBlock(name, func, description);
        }


        public static ExecutionPipeline CreatePipeline(
            string name,
            string? description = null)
        {
            return new ExecutionPipeline(name, description);
        }

        public static ExecutionPipeline Compose(
            string name,
            params IBlock[] blocks)
        {
            var pipeline = new ExecutionPipeline(name);
            foreach (var block in blocks)
                pipeline.AddBlock(block);
            return pipeline;
        }


        public static string ToDiagnosticString(this PipelineResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"╔═══ 管线执行报告 ═══");
            sb.AppendLine($"║ 整体状态: {(result.OverallSuccess ? "✓ 成功" : "✗ 失败")}");
            sb.AppendLine($"║ 总耗时: {result.TotalDuration.TotalMilliseconds:F1}ms");
            sb.AppendLine($"║ 成功: {result.SuccessCount} | 失败: {result.FailedCount} | 跳过: {result.SkippedCount}");
            sb.AppendLine($"╠═══ 块诊断详情 ═══");

            foreach (var diag in result.Diagnostics)
            {
                var statusSymbol = diag.Status switch
                {
                    BlockStatus.Success => "✓",
                    BlockStatus.Failed => "✗",
                    BlockStatus.Skipped => "⊘",
                    BlockStatus.Aborted => "⊝",
                    _ => "?"
                };

                sb.AppendLine($"║ [{statusSymbol}] {diag.BlockName}");
                sb.AppendLine($"║   状态: {diag.Status} | 耗时: {diag.Duration.TotalMilliseconds:F1}ms");
                if (diag.Message != null)
                    sb.AppendLine($"║   消息: {diag.Message}");
                if (diag.Exception != null)
                    sb.AppendLine($"║   异常: {diag.Exception.Message}");
            }

            sb.AppendLine($"╚═══ 报告结束 ═══");
            return sb.ToString();
        }

        public static BlockDiagnostic[] GetFailedBlocks(this PipelineResult result)
            => result.Diagnostics.Where(d => d.Status == BlockStatus.Failed).ToArray();

        public static BlockDiagnostic[] GetSuccessfulBlocks(this PipelineResult result)
            => result.Diagnostics.Where(d => d.Status == BlockStatus.Success).ToArray();

        public static BlockDiagnostic[] GetSkippedBlocks(this PipelineResult result)
            => result.Diagnostics.Where(d => d.Status == BlockStatus.Skipped).ToArray();
    }
}
