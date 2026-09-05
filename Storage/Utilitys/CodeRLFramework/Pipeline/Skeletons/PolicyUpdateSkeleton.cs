
#region USINGS
using CodeRLFramework.RL;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class PolicyUpdateSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly PolicyGradient _policyGradient;

        public PolicyUpdateSkeleton(PolicyGradient policyGradient)
            : base("PolicyUpdateSkeleton", "记录轨迹并使用 REINFORCE 算法更新策略")
        {
            _policyGradient = policyGradient ?? throw new ArgumentNullException(nameof(policyGradient));
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (input.DecodedFunctionNames == null || input.DecodedFunctionNames.Count == 0)
            {
                _policyGradient.EndTrajectory();

                context.Items["PolicyUpdateSkeleton.Info"] =
                    $"策略更新完成（空轨迹）: 平均奖励={_policyGradient.AverageReward:F4}, " +
                    $"最佳平均奖励={_policyGradient.BestAverageReward:F4}";

                return Task.FromResult(input);
            }

            RecordTrajectory(input);

            _policyGradient.EndTrajectory();

            context.Items["PolicyUpdateSkeleton.Info"] =
                $"策略更新完成: 平均奖励={_policyGradient.AverageReward:F4}, " +
                $"最佳平均奖励={_policyGradient.BestAverageReward:F4}";

            return Task.FromResult(input);
        }


        private void RecordTrajectory(CodeRLContext input)
        {
            var functionNames = input.DecodedFunctionNames;
            double finalReward = input.Reward;

            for (int i = 0; i < functionNames.Count; i++)
            {
                int actionId = FindFunctionIndex(functionNames[i], input);

                double[] state = new double[]
                {
                    i / (double)Math.Max(input.MaxStepsPerEpisode, 1),
                    functionNames.Count / (double)Math.Max(input.MaxStepsPerEpisode, 1),
                    finalReward > 0 ? 1.0 : 0.0
                };

                double stepReward = functionNames.Count > 0 ? finalReward / functionNames.Count : 0;
                _policyGradient.RecordStep(actionId, stepReward, state);
            }
        }

        private static int FindFunctionIndex(string functionName, CodeRLContext input)
        {
            if (input.FunctionSpace == null) return 0;

            for (int i = 0; i < input.FunctionSpace.Functions.Count; i++)
            {
                if (input.FunctionSpace.Functions[i].Name.Equals(functionName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }
    }
}
