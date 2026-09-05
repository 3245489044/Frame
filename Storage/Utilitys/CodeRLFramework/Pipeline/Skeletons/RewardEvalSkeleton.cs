
#region USINGS
using CodeRLFramework.RL;
using SkeletonFlow;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class RewardEvalSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly RewardFunction _rewardFunction;

        public RewardEvalSkeleton(RewardFunction rewardFunction)
            : base("RewardEvalSkeleton", "计算生成代码的质量奖励")
        {
            _rewardFunction = rewardFunction ?? throw new ArgumentNullException(nameof(rewardFunction));
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrEmpty(input.GeneratedCode))
                throw new InvalidOperationException("GeneratedCode 未设置，请先执行 ConvertSkeleton");

            double baseReward = _rewardFunction.Calculate(
                input.GeneratedCode,
                input.TestCode ?? "");

            double graphReward = 0;
            if (input.GraphSnapshot != null && input.DecodedFunctionNames != null)
            {
                var validFunctions = new HashSet<string>(
                    input.GraphSnapshot.Nodes
                        .Where(n => !string.IsNullOrEmpty(n.Name))
                        .Select(n => n.Name!));
                graphReward = _rewardFunction.CalculateStructureReward(
                    input.DecodedFunctionNames, validFunctions);
            }

            double navReward = 0;
            if (input.NavContext != null)
            {
                navReward = CalculateNavigationQualityReward(input.NavContext);
            }

            double placeholderReward = CalculatePlaceholderIntegrityReward(input);

            double wBase = input.Weights?.BaseRewardWeight ?? 0.4;
            double wGraph = input.Weights?.GraphStructureWeight ?? 0.3;
            double wNav = input.Weights?.NavigationQualityWeight ?? 0.2;
            double wPlaceholder = input.Weights?.PlaceholderIntegrityWeight ?? 0.1;

            double reward = wBase * baseReward
                          + wGraph * graphReward
                          + wNav * navReward
                          + wPlaceholder * placeholderReward;

            input.Reward = reward;
            input.RewardHistory.Add(reward);

            if (reward > input.BestReward)
            {
                input.BestReward = reward;
                input.BestCode = input.GeneratedCode;
            }


            var infoParts = new List<string>
            {
                $"奖励={reward:F4}",
                $"最佳={input.BestReward:F4}",
                $"收敛={input.IsConverged}"
            };

            if (input.GraphSnapshot != null)
                infoParts.Add($"图节点={input.GraphSnapshot.Nodes.Count}");

            if (input.NavContext != null)
                infoParts.Add($"导航步={input.NavContext.CurrentStep}");

            context.Items["RewardEvalSkeleton.Info"] = string.Join(", ", infoParts);

            return Task.FromResult(input);
        }

        private static double CalculateNavigationQualityReward(
            Models.GraphNavigationContext navContext)
        {
            if (navContext == null || navContext.Steps.Count == 0)
                return 0;

            double reward = 0;

            int stepCount = navContext.CurrentStep;
            if (stepCount >= 3 && stepCount <= 15)
                reward += 0.3;
            else if (stepCount >= 1 && stepCount <= 20)
                reward += 0.15;
            else
                reward -= 0.1;

            bool hasCycle = navContext.VisitedSet.Count < navContext.VisitedPath.Count;
            if (!hasCycle)
                reward += 0.25;
            else
                reward -= 0.2;

            if (navContext.Steps.Count > 0)
            {
                double avgConfidence = navContext.Steps.Average(s => s.Confidence);
                reward += avgConfidence * 0.25;
            }

            if (navContext.Steps.Count > 0)
            {
                double fallbackRatio = (double)navContext.Steps.Count(s => s.IsFallback)
                    / navContext.Steps.Count;
                reward += (1.0 - fallbackRatio) * 0.2;
            }

            return Math.Clamp(reward, -0.5, 1.0);
        }

        private static double CalculatePlaceholderIntegrityReward(CodeRLContext input)
        {
            double reward = 0;

            if (string.IsNullOrEmpty(input.GeneratedCode))
                return 0;

            bool hasPlaceholder = Placeholder.PlaceholderDefinition
                .ContainsPlaceholder(input.GeneratedCode);

            if (!hasPlaceholder)
            {
                return 0;
            }

            var placeholders = Placeholder.PlaceholderDefinition
                .ParseAll(input.GeneratedCode);

            if (placeholders.Count == 0)
                return 0;

            var graphNodeNames = new HashSet<string>();
            if (input.GraphSnapshot != null)
            {
                foreach (var node in input.GraphSnapshot.Nodes)
                {
                    if (!string.IsNullOrEmpty(node.Name))
                        graphNodeNames.Add(node.Name);
                }
            }

            int validNodeRefs = 0;
            int totalNodeRefs = 0;

            foreach (var ph in placeholders)
            {
                if (ph.Type == Placeholder.PlaceholderType.Node && !string.IsNullOrEmpty(ph.Name))
                {
                    totalNodeRefs++;
                    if (graphNodeNames.Contains(ph.Name))
                        validNodeRefs++;
                }
            }

            if (totalNodeRefs > 0)
            {
                double refRatio = (double)validNodeRefs / totalNodeRefs;
                reward += refRatio * 0.5;
            }

            int validPrevStepRefs = 0;
            int totalPrevStepRefs = 0;

            foreach (var ph in placeholders)
            {
                if (ph.Type == Placeholder.PlaceholderType.PrevStep)
                {
                    totalPrevStepRefs++;
                    if (input.NavContext != null && input.NavContext.CurrentStep > 0)
                        validPrevStepRefs++;
                }
            }

            if (totalPrevStepRefs > 0)
            {
                double prevStepRatio = (double)validPrevStepRefs / totalPrevStepRefs;
                reward += prevStepRatio * 0.3;
            }

            int validInputRefs = 0;
            int totalInputRefs = 0;

            foreach (var ph in placeholders)
            {
                if (ph.Type == Placeholder.PlaceholderType.Input)
                {
                    totalInputRefs++;
                    if (!string.IsNullOrEmpty(input.TemplateCode))
                        validInputRefs++;
                }
            }

            if (totalInputRefs > 0)
            {
                double inputRatio = (double)validInputRefs / totalInputRefs;
                reward += inputRatio * 0.2;
            }

            return Math.Clamp(reward, -0.5, 1.0);
        }

    }
}
