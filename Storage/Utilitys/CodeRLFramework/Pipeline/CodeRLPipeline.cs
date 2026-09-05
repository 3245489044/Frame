
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using CodeRLFramework.Pipeline.Skeletons;
using SkeletonFlow;
using WorkFlowMethod.CentralPipeline;
using WorkFlowMethod.GraphFramework.Snapshots;
#endregion

namespace CodeRLFramework.Pipeline
{
    public class CodeRLPipelineResult
    {
        public bool IsSuccess { get; set; }

        public string? ErrorMessage { get; set; }

        public int TotalEpisodes { get; set; }

        public double BestReward { get; set; }

        public string? BestCode { get; set; }

        public double AverageReward { get; set; }

        public TimeSpan TrainingTime { get; set; }

        public double[] RewardHistory { get; set; } = Array.Empty<double>();

        public bool IsConverged { get; set; }

        public string? GeneratedCode { get; set; }

        public GraphSnapshot? GraphSnapshot { get; set; }
    }

    public class CodeRLTrainingProgress
    {
        public int Episode { get; set; }

        public double Reward { get; set; }

        public double BestReward { get; set; }

        public double AverageReward { get; set; }

        public bool IsConverged { get; set; }
    }

    public class CodeRLPipeline : IDisposable
    {
        private readonly WorkflowCentralPipeline _centralPipeline;
        private readonly CodeRLPipelineFactory _factory;
        private bool _disposed;

        public WorkflowCentralPipeline InnerPipeline => _centralPipeline;

        public CodeRLPipelineFactory Factory => _factory;

        public string Name => _centralPipeline.Name;

        public PipelineStatus Status => _centralPipeline.Status;

        public CodeRLPipeline(
            string? name = null,
            VectorExtractor? extractor = null,
            CodeGenerationAgent? agent = null,
            VectorToCodeConverter? converter = null,
            RewardFunction? rewardFunction = null,
            PolicyGradient? policyGradient = null)
        {
            _centralPipeline = new WorkflowCentralPipeline(name ?? "CodeRLPipeline");
            _factory = new CodeRLPipelineFactory(extractor, agent, converter, rewardFunction, policyGradient);

            _factory.RegisterToCentralPipeline(_centralPipeline);
        }

        public CodeRLPipeline(WorkflowCentralPipeline centralPipeline, CodeRLPipelineFactory factory)
        {
            _centralPipeline = centralPipeline ?? throw new ArgumentNullException(nameof(centralPipeline));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public CodeRLPipeline(CodeRLConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _centralPipeline = new WorkflowCentralPipeline(config.Pipeline.Name);
            _factory = new CodeRLPipelineFactory();
            _factory.RegisterToCentralPipeline(_centralPipeline);
        }

        public CodeRLPipeline(CodeRLConfig config, CodeRLPipelineFactory factory)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _centralPipeline = new WorkflowCentralPipeline(config.Pipeline.Name);
            _factory = factory;
            _factory.RegisterToCentralPipeline(_centralPipeline);
        }

        public CodeRLContext ExecuteEpisode(CodeRLContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var pipeline = _factory.CreateSingleEpisodePipeline(enablePlaceholder: true);
            using var skelContext = new SkeletonContext();

            var result = pipeline.Execute(context, skelContext);
            result.Episode++;

            return result;
        }

        public async Task<CodeRLContext> ExecuteEpisodeAsync(CodeRLContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var pipeline = _factory.CreateSingleEpisodePipeline(enablePlaceholder: true);
            using var skelContext = new SkeletonContext();

            var result = await pipeline.ExecuteAsync(context, skelContext).ConfigureAwait(false);
            result.Episode++;

            return result;
        }

        public CodeRLPipelineResult Train(
            string codebasePath,
            string templateCode,
            string? testCode = null,
            int maxEpisodes = 100,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            var context = new CodeRLContext
            {
                CodebasePath = codebasePath,
                TemplateCode = templateCode,
                TestCode = testCode,
                MaxStepsPerEpisode = 50,
                ConvergenceThreshold = 0.01,
                ConvergenceWindow = 10
            };

            return TrainInternal(context, maxEpisodes, onProgress, useAsync: false).GetAwaiter().GetResult();
        }

        public CodeRLPipelineResult Train(
            CodeRLConfig config,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var context = new CodeRLContext();
            CodeRLPipelineFactory.ApplyConfigToContext(context, config);

            return TrainInternal(context, config.Training.MaxEpisodes, onProgress, useAsync: false).GetAwaiter().GetResult();
        }

        public Task<CodeRLPipelineResult> TrainAsync(
            string codebasePath,
            string templateCode,
            string? testCode = null,
            int maxEpisodes = 100,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            var context = new CodeRLContext
            {
                CodebasePath = codebasePath,
                TemplateCode = templateCode,
                TestCode = testCode,
                MaxStepsPerEpisode = 50,
                ConvergenceThreshold = 0.01,
                ConvergenceWindow = 10
            };

            return TrainInternal(context, maxEpisodes, onProgress, useAsync: true);
        }

        public Task<CodeRLPipelineResult> TrainAsync(
            CodeRLConfig config,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var context = new CodeRLContext();
            CodeRLPipelineFactory.ApplyConfigToContext(context, config);

            return TrainInternal(context, config.Training.MaxEpisodes, onProgress, useAsync: true);
        }

        private async Task<CodeRLPipelineResult> TrainInternal(
            CodeRLContext context,
            int maxEpisodes,
            Action<CodeRLTrainingProgress>? onProgress,
            bool useAsync)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var fullPipeline = _factory.CreateTrainingPipeline(enablePlaceholder: true);
            using var skelContext = new SkeletonContext();

            var result = useAsync
                ? await fullPipeline.ExecuteAsync(context, skelContext).ConfigureAwait(false)
                : fullPipeline.Execute(context, skelContext);

            if (!result.IsConverged && result.Episode < maxEpisodes)
            {
                for (int ep = result.Episode; ep < maxEpisodes; ep++)
                {
                    result = useAsync
                        ? await ExecuteEpisodeAsync(result).ConfigureAwait(false)
                        : ExecuteEpisode(result);

                    onProgress?.Invoke(new CodeRLTrainingProgress
                    {
                        Episode = result.Episode,
                        Reward = result.Reward,
                        BestReward = result.BestReward,
                        AverageReward = result.RewardHistory.DefaultIfEmpty(0).Average(),
                        IsConverged = result.IsConverged
                    });

                    if (result.IsConverged)
                        break;
                }
            }

            stopwatch.Stop();

            return new CodeRLPipelineResult
            {
                IsSuccess = true,
                TotalEpisodes = result.Episode,
                BestReward = result.BestReward,
                BestCode = result.BestCode,
                AverageReward = result.RewardHistory.DefaultIfEmpty(0).Average(),
                TrainingTime = stopwatch.Elapsed,
                RewardHistory = result.RewardHistory.ToArray(),
                IsConverged = result.IsConverged,
                GeneratedCode = result.GeneratedCode,
                GraphSnapshot = result.GraphSnapshot
            };
        }

        public PipelineExecutionResult ExecuteWorkflow(WorkflowContext workflowContext)
        {
            return _centralPipeline.Execute(workflowContext);
        }

        public Task<PipelineExecutionResult> ExecuteWorkflowAsync(WorkflowContext workflowContext)
        {
            return _centralPipeline.ExecuteAsync(workflowContext);
        }

        public string GetDescription() => _centralPipeline.GetPipelineDescription();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _centralPipeline.Dispose();
        }
    }
}
