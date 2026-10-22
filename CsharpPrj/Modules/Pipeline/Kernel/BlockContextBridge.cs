#nullable enable

using System.Threading;
using BasicMethod.Block;

namespace Modules.Pipeline.Kernel
{
    /// <summary>
    /// PipelineContext ↔ BlockContext 桥接：共享 SharedData，账本写入回写旧字段。
    /// 不做继承（PipelineContext 必须保留 SkeletonContext 基类）。
    /// </summary>
    internal static class BlockContextBridge
    {
        public static BlockContext Create(
            IPipelineContext context,
            CancellationToken cancellationToken)
        {
            return new BlockContext(
                context.SharedData,
                cancellationToken,
                diagnostic => SyncLedger(context, diagnostic));
        }

        private static void SyncLedger(
            IPipelineContext context,
            BlockDiagnostic diagnostic)
        {
            var success = diagnostic.Outcome is BlockOutcome.Success or BlockOutcome.Skipped;
            context.SetStageResult(
                diagnostic.BlockName,
                success,
                success ? null : diagnostic.Message);
        }
    }
}
