
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
#endregion

namespace CodeRLFramework
{
    public class CodeRLManager : IDisposable
    {
        private VectorExtractor? _extractor;
        private CodeTokenizer? _tokenizer;
        private CodeGenerationAgent? _agent;
        private VectorToCodeConverter? _converter;
        private RewardFunction? _rewardFunction;
        private PolicyGradient? _policyGradient;
        private FunctionVectorSpace? _functionSpace;

        private CodeRLConfig _config;
        private bool _configLoaded;

        private CodeRLPipeline? _pipeline;
        private bool _disposed;

        private readonly object _lock = new();


        public CodeRLManager()
        {
            _config = CodeRLConfig.GetDefault();
            _configLoaded = false;
        }

        public CodeRLManager(CodeRLConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _configLoaded = true;
        }

        public CodeRLManager(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentException("配置文件路径不能为空", nameof(configPath));

            _config = CodeRLConfig.FromFile(configPath);
            _configLoaded = true;
        }

        public CodeRLManager(CodeRLPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _config = CodeRLConfig.GetDefault();
            _configLoaded = false;
        }


        public CodeRLConfig Config => _config;

        public CodeRLPipeline Pipeline
        {
            get
            {
                EnsurePipeline();
                return _pipeline!;
            }
        }

        public bool IsInitialized => _pipeline != null;


        public CodeRLManager UseExtractor(VectorExtractor extractor)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            return this;
        }

        public CodeRLManager UseTokenizer(CodeTokenizer tokenizer)
        {
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            return this;
        }

        public CodeRLManager UseAgent(CodeGenerationAgent agent)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            return this;
        }

        public CodeRLManager UseConverter(VectorToCodeConverter converter)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            return this;
        }

        public CodeRLManager UseRewardFunction(RewardFunction rewardFunction)
        {
            _rewardFunction = rewardFunction ?? throw new ArgumentNullException(nameof(rewardFunction));
            return this;
        }

        public CodeRLManager UsePolicyGradient(PolicyGradient policyGradient)
        {
            _policyGradient = policyGradient ?? throw new ArgumentNullException(nameof(policyGradient));
            return this;
        }

        public CodeRLManager UseFunctionSpace(FunctionVectorSpace functionSpace)
        {
            _functionSpace = functionSpace ?? throw new ArgumentNullException(nameof(functionSpace));
            return this;
        }


        public CodeRLManager Configure(Action<CodeRLConfig> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            configure(_config);
            _configLoaded = true;
            return this;
        }

        public CodeRLManager LoadConfig(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentException("配置文件路径不能为空", nameof(configPath));

            _config = CodeRLConfig.FromFile(configPath);
            _configLoaded = true;
            return this;
        }

        public CodeRLManager LoadConfigFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON 字符串不能为空", nameof(json));

            _config = CodeRLConfig.FromJson(json);
            _configLoaded = true;
            return this;
        }


        public Task<CodeRLPipelineResult> TrainAsync(
            string? codebasePath = null,
            string? templateCode = null,
            string? testCode = null,
            int? maxEpisodes = null,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            EnsurePipeline();

            if (codebasePath != null)
                _config.Training.CodebasePath = codebasePath;
            if (templateCode != null)
                _config.Training.TemplateCode = templateCode;
            if (testCode != null)
                _config.Training.TestCode = testCode;

            if (maxEpisodes.HasValue)
                _config.Training.MaxEpisodes = maxEpisodes.Value;

            return _pipeline!.TrainAsync(_config, onProgress);
        }

        public CodeRLPipelineResult Train(
            string? codebasePath = null,
            string? templateCode = null,
            string? testCode = null,
            int? maxEpisodes = null,
            Action<CodeRLTrainingProgress>? onProgress = null)
        {
            return TrainAsync(codebasePath, templateCode, testCode, maxEpisodes, onProgress)
                .GetAwaiter().GetResult();
        }

        public Task<CodeRLContext> TrainEpisodeAsync(
            string? codebasePath = null,
            string? templateCode = null,
            string? testCode = null)
        {
            EnsurePipeline();

            var context = BuildContext(codebasePath, templateCode, testCode);
            return _pipeline!.ExecuteEpisodeAsync(context);
        }

        public CodeRLContext TrainEpisode(
            string? codebasePath = null,
            string? templateCode = null,
            string? testCode = null)
        {
            return TrainEpisodeAsync(codebasePath, templateCode, testCode)
                .GetAwaiter().GetResult();
        }

        public Task<CodeRLContext> TrainEpisodeAsync(CodeRLContext context)
        {
            EnsurePipeline();
            return _pipeline!.ExecuteEpisodeAsync(context);
        }

        public CodeRLContext TrainEpisode(CodeRLContext context)
        {
            EnsurePipeline();
            return _pipeline!.ExecuteEpisode(context);
        }


        public CodeRLManager CreatePipeline()
        {
            lock (_lock)
            {
                _pipeline?.Dispose();

                _pipeline = BuildPipeline();
            }
            return this;
        }

        public CodeRLManager Reset()
        {
            lock (_lock)
            {
                _pipeline?.Dispose();
                _pipeline = null;
                _extractor = null;
                _tokenizer = null;
                _agent = null;
                _converter = null;
                _rewardFunction = null;
                _policyGradient = null;
                _functionSpace = null;
                _config = CodeRLConfig.GetDefault();
                _configLoaded = false;
            }
            return this;
        }

        public string GetStatus()
        {
            return $"CodeRLManager [{(IsInitialized ? "已初始化" : "未初始化")}] " +
                   $"配置: {(_configLoaded ? "已加载" : "默认")}, " +
                   $"管道: {(_pipeline?.Name ?? "N/A")}";
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _pipeline?.Dispose();
            _pipeline = null;
        }


        private void EnsurePipeline()
        {
            if (_pipeline == null)
            {
                lock (_lock)
                {
                    if (_pipeline == null)
                    {
                        _pipeline = BuildPipeline();
                    }
                }
            }
        }

        private CodeRLPipeline BuildPipeline()
        {
            EnsureDefaultComponents();

            var factory = new CodeRLPipelineFactory(
                extractor: _extractor,
                agent: _agent,
                converter: _converter,
                rewardFunction: _rewardFunction,
                policyGradient: _policyGradient,
                functionSpace: _functionSpace,
                tokenizer: _tokenizer,
                actionCount: _functionSpace?.Functions.Count ?? 10
            );

            var pipeline = new CodeRLPipeline(_config, factory);
            return pipeline;
        }

        private void EnsureDefaultComponents()
        {
            _functionSpace ??= new FunctionVectorSpace
            {
                Source = "default",
                VectorDimension = 340,
                Weight = 1.0
            };

            _tokenizer ??= new CodeTokenizer(_functionSpace);

            _agent ??= new CodeGenerationAgent(_tokenizer);

            _converter ??= new VectorToCodeConverter(_functionSpace, _tokenizer);

            _extractor ??= new VectorExtractor();

            _rewardFunction ??= new RewardFunction();

            _policyGradient ??= new PolicyGradient(
                Math.Max(_functionSpace.Functions.Count, 10));
        }

        private static CodeRLContext BuildContext(
            string? codebasePath,
            string? templateCode,
            string? testCode)
        {
            return new CodeRLContext
            {
                CodebasePath = codebasePath,
                TemplateCode = templateCode,
                TestCode = testCode
            };
        }
    }
}
