
#region USINGS
using SkeletonFlow;
using WorkFlowMethod.CentralPipeline;
#endregion

namespace CodeRLFramework.Pipeline
{
    public class CodeRLAdapterSkeleton : SkeletonBase<WorkflowContext, WorkflowContext>
    {
        private readonly CodeRLPipelineFactory _factory;

        public CodeRLAdapterSkeleton(CodeRLPipelineFactory factory)
            : base("CodeRLAdapterSkeleton", "CodeRL ↔ WorkflowContext 适配器")
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        protected override WorkflowContext ExecuteCore(WorkflowContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<WorkflowContext> ExecuteCoreAsync(WorkflowContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var codebasePath = input.GetData<string>("CodeRL.CodebasePath");
            var templateCode = input.GetData<string>("CodeRL.TemplateCode");
            var testCode = input.GetData<string>("CodeRL.TestCode");
            var maxEpisodes = input.GetData<int>("CodeRL.MaxEpisodes");

            var codeRLContext = new CodeRLContext(input)
            {
                CodebasePath = codebasePath,
                TemplateCode = templateCode,
                TestCode = testCode,
                MaxStepsPerEpisode = input.GetData<int>("CodeRL.MaxStepsPerEpisode"),
                ConvergenceThreshold = input.GetData<double>("CodeRL.ConvergenceThreshold"),
                ConvergenceWindow = input.GetData<int>("CodeRL.ConvergenceWindow")
            };

            var pipeline = _factory.CreateTrainingPipeline();
            var skelContext = new SkeletonContext();
            skelContext.CancellationToken = context.CancellationToken;

            var result = pipeline.Execute(codeRLContext, skelContext);

            input.SetData("CodeRL.Episode", result.Episode);
            input.SetData("CodeRL.Reward", result.Reward);
            input.SetData("CodeRL.BestReward", result.BestReward);
            input.SetData("CodeRL.BestCode", result.BestCode ?? "");
            input.SetData("CodeRL.IsConverged", result.IsConverged);
            input.SetData("CodeRL.RewardHistory", result.RewardHistory.ToArray());
            input.SetData("CodeRL.GeneratedCode", result.GeneratedCode ?? "");

            if (result.GraphSnapshot != null)
            {
                input.SetGraphSnapshot(result.GraphSnapshot);
            }

            context.Items["CodeRLAdapterSkeleton.Info"] =
                $"CodeRL 训练完成: 轮数={result.Episode}, " +
                $"奖励={result.Reward:F4}, 最佳奖励={result.BestReward:F4}, " +
                $"已收敛={result.IsConverged}";

            return Task.FromResult(input);
        }
    }
}
