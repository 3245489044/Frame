
#region USINGS
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class ConvergenceCheckSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        public ConvergenceCheckSkeleton()
            : base("ConvergenceCheckSkeleton", "检查训练是否已收敛")
        {
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            bool converged = CheckConvergence(input);
            input.IsConverged = converged;

            if (converged)
            {
                context.Items["ConvergenceCheckSkeleton.Info"] =
                    $"训练已收敛: 轮数={input.Episode}, " +
                    $"最佳奖励={input.BestReward:F4}, " +
                    $"平均奖励={input.RewardHistory.DefaultIfEmpty(0).Average():F4}";
            }
            else
            {
                context.Items["ConvergenceCheckSkeleton.Info"] =
                    $"训练未收敛: 轮数={input.Episode}, " +
                    $"最近奖励={string.Join(", ", input.RewardHistory.TakeLast(5).Select(r => $"{r:F4}"))}";
            }

            return Task.FromResult(input);
        }

        private static bool CheckConvergence(CodeRLContext input)
        {
            if (input.RewardHistory.Count < input.ConvergenceWindow)
                return false;

            var recent = input.RewardHistory.TakeLast(input.ConvergenceWindow).ToArray();
            double maxDeviation = recent.Max() - recent.Min();

            return maxDeviation <= input.ConvergenceThreshold + 1e-10 && recent.Average() > 0.5;
        }

    }
}
