
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using CodeRLFramework.Pipeline.Skeletons;
using SkeletonFlow;
using WorkFlowMethod.CentralPipeline;
#endregion

namespace CodeRLFramework.Pipeline
{
    public class CodeRLPipelineFactory
    {
        private VectorExtractor _extractor;
        private CodeGenerationAgent _agent;
        private VectorToCodeConverter _converter;
        private readonly RewardFunction _rewardFunction;
        private PolicyGradient _policyGradient;

        public CodeRLPipelineFactory(
            VectorExtractor? extractor = null,
            CodeGenerationAgent? agent = null,
            VectorToCodeConverter? converter = null,
            RewardFunction? rewardFunction = null,
            PolicyGradient? policyGradient = null,
            FunctionVectorSpace? functionSpace = null,
            CodeTokenizer? tokenizer = null,
            int actionCount = 10)
        {
            _extractor = extractor ?? new VectorExtractor();

            var defaultFunctionSpace = functionSpace ?? new FunctionVectorSpace();
            var defaultTokenizer = tokenizer ?? new CodeTokenizer(defaultFunctionSpace);

            _agent = agent ?? new CodeGenerationAgent(defaultTokenizer);
            _converter = converter ?? new VectorToCodeConverter(defaultFunctionSpace, defaultTokenizer);
            _rewardFunction = rewardFunction ?? new RewardFunction();
            _policyGradient = policyGradient ?? new PolicyGradient(actionCount);
        }

        public CodeExtractSkeleton CreateCodeExtractSkeleton()
            => new(_extractor);

        public VectorizeSkeleton CreateVectorizeSkeleton()
            => new(_extractor);

        public GraphValidateSkeleton CreateGraphValidateSkeleton()
            => new();

        public TokenizeSkeleton CreateTokenizeSkeleton()
            => new();

        public LLMNavSelectSkeleton CreateLLMNavSelectSkeleton()
            => new(_agent);

        public CodeGenSkeleton CreateCodeGenSkeleton()
            => new(_agent, CreateLLMNavSelectSkeleton());

        public ConvertSkeleton CreateConvertSkeleton()
            => new(_converter);

        public RewardEvalSkeleton CreateRewardEvalSkeleton()
            => new(_rewardFunction);

        public PolicyUpdateSkeleton CreatePolicyUpdateSkeleton()
            => new(_policyGradient);

        public ConvergenceCheckSkeleton CreateConvergenceCheckSkeleton()
            => new();

        public PlaceholderResolveSkeleton CreatePlaceholderResolveSkeleton()
            => new();

        public ISkeleton<CodeRLContext, CodeRLContext> CreateTrainingPipeline(bool enablePlaceholder = true)
        {
            var pipeline = CreateCodeExtractSkeleton()
                .Then(CreateVectorizeSkeleton())
                .Then(CreateGraphValidateSkeleton())
                .Then(CreateTokenizeSkeleton())
                .Then(CreateCodeGenSkeleton())
                .Then(CreateConvertSkeleton());

            if (enablePlaceholder)
            {
                pipeline = pipeline
                    .Then(CreatePlaceholderResolveSkeleton());
            }

            pipeline = pipeline
                .Then(CreateRewardEvalSkeleton())
                .Then(CreatePolicyUpdateSkeleton())
                .Then(CreateConvergenceCheckSkeleton());

            return pipeline;
        }

        public ISkeleton<CodeRLContext, CodeRLContext> CreateSingleEpisodePipeline(bool enablePlaceholder = true)
        {
            var pipeline = CreateTokenizeSkeleton()
                .Then(CreateCodeGenSkeleton())
                .Then(CreateConvertSkeleton());

            if (enablePlaceholder)
            {
                pipeline = pipeline
                    .Then(CreatePlaceholderResolveSkeleton());
            }

            pipeline = pipeline
                .Then(CreateRewardEvalSkeleton())
                .Then(CreatePolicyUpdateSkeleton());

            return pipeline;
        }

        public ISkeleton<CodeRLContext, CodeRLContext> CreateTrainingPipeline(CodeRLConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var skeletonConfig = config.Skeletons;
            ISkeleton<CodeRLContext, CodeRLContext>? pipeline = null;

            if (skeletonConfig.IsEnabled("CodeExtract"))
                pipeline = AppendSkeleton(pipeline, CreateCodeExtractSkeleton());

            if (skeletonConfig.IsEnabled("Vectorize"))
                pipeline = AppendSkeleton(pipeline, CreateVectorizeSkeleton());

            if (skeletonConfig.IsEnabled("GraphValidate"))
                pipeline = AppendSkeleton(pipeline, CreateGraphValidateSkeleton());

            if (skeletonConfig.IsEnabled("Tokenize"))
                pipeline = AppendSkeleton(pipeline, CreateTokenizeSkeleton());

            if (skeletonConfig.IsEnabled("CodeGen"))
                pipeline = AppendSkeleton(pipeline, CreateCodeGenSkeleton());

            if (skeletonConfig.IsEnabled("Convert"))
                pipeline = AppendSkeleton(pipeline, CreateConvertSkeleton());

            if (skeletonConfig.IsEnabled("RewardEval"))
                pipeline = AppendSkeleton(pipeline, CreateRewardEvalSkeleton());

            if (skeletonConfig.IsEnabled("PolicyUpdate"))
                pipeline = AppendSkeleton(pipeline, CreatePolicyUpdateSkeleton());

            if (skeletonConfig.IsEnabled("ConvergenceCheck"))
                pipeline = AppendSkeleton(pipeline, CreateConvergenceCheckSkeleton());

            return pipeline ?? throw new InvalidOperationException("配置中未启用任何 Skeleton");
        }

        public ISkeleton<CodeRLContext, CodeRLContext> CreateSingleEpisodePipeline(CodeRLConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var skeletonConfig = config.Skeletons;
            ISkeleton<CodeRLContext, CodeRLContext>? pipeline = null;

            if (skeletonConfig.IsEnabled("Tokenize"))
                pipeline = AppendSkeleton(pipeline, CreateTokenizeSkeleton());

            if (skeletonConfig.IsEnabled("CodeGen"))
                pipeline = AppendSkeleton(pipeline, CreateCodeGenSkeleton());

            if (skeletonConfig.IsEnabled("Convert"))
                pipeline = AppendSkeleton(pipeline, CreateConvertSkeleton());

            if (skeletonConfig.IsEnabled("RewardEval"))
                pipeline = AppendSkeleton(pipeline, CreateRewardEvalSkeleton());

            if (skeletonConfig.IsEnabled("PolicyUpdate"))
                pipeline = AppendSkeleton(pipeline, CreatePolicyUpdateSkeleton());

            return pipeline ?? throw new InvalidOperationException("配置中未启用任何 Skeleton");
        }

        private static ISkeleton<CodeRLContext, CodeRLContext> AppendSkeleton(
            ISkeleton<CodeRLContext, CodeRLContext>? current,
            ISkeleton<CodeRLContext, CodeRLContext> next)
        {
            return current == null ? next : new CompositeSkeleton<CodeRLContext, CodeRLContext, CodeRLContext>(current, next);
        }

        public static void ApplyConfigToContext(CodeRLContext context, CodeRLConfig config)
        {
            if (context == null || config == null) return;

            context.Weights = config.Weights;

            context.MaxStepsPerEpisode = config.Training.MaxStepsPerEpisode;
            context.ConvergenceThreshold = config.Training.ConvergenceThreshold;
            context.ConvergenceWindow = config.Training.ConvergenceWindow;

            if (!string.IsNullOrEmpty(config.Training.CodebasePath))
                context.CodebasePath = config.Training.CodebasePath;
            if (!string.IsNullOrEmpty(config.Training.TemplateCode))
                context.TemplateCode = config.Training.TemplateCode;
            if (!string.IsNullOrEmpty(config.Training.TestCode))
                context.TestCode = config.Training.TestCode;
        }

        public void RegisterToCentralPipeline(
            WorkflowCentralPipeline centralPipeline,
            PipelineStage stage = PipelineStage.GraphDefinition)
        {
            var adapter = new CodeRLAdapterSkeleton(this);

            centralPipeline.Registry.Register(stage, adapter);
        }

        public void SetAgent(CodeGenerationAgent agent)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        }

        public void SetConverter(VectorToCodeConverter converter)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        }

        public void SetPolicyGradient(PolicyGradient policyGradient)
        {
            _policyGradient = policyGradient ?? throw new ArgumentNullException(nameof(policyGradient));
        }
    }
}
