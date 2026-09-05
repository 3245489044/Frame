
using System.Text.Json;
using CodeRLFramework.Input;
using CodeRLFramework.Pipeline;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Snapshots;

namespace CodeRLFramework.Tests.Fixtures
{
    public class TestParameterResolver : IDisposable
    {
        private readonly string _configPath;
        private JsonDocument? _configDoc;
        private readonly SkeletonContext _context;

        public TestParameterResolver(string? configPath = null)
        {
            _configPath = configPath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "test_parameters.json");
            _context = new SkeletonContext();
            LoadConfig();
        }

        public SkeletonContext Context => _context;

        private void LoadConfig()
        {
            var paths = new[]
            {
                _configPath,
                Path.Combine(Directory.GetCurrentDirectory(), "test_parameters.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "test_parameters.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "test_parameters.json")
            };

            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    var json = File.ReadAllText(fullPath);
                    _configDoc = JsonDocument.Parse(json);
                    return;
                }
            }

            _configDoc = JsonDocument.Parse("{}");
        }

        public string? GetString(params string[] path)
        {
            if (_configDoc == null) return null;
            var element = NavigateTo(path);
            return element?.GetString();
        }

        public int GetInt(int defaultValue, params string[] path)
        {
            if (_configDoc == null) return defaultValue;
            var element = NavigateTo(path);
            return element?.GetInt32() ?? defaultValue;
        }

        public double GetDouble(double defaultValue, params string[] path)
        {
            if (_configDoc == null) return defaultValue;
            var element = NavigateTo(path);
            return element?.GetDouble() ?? defaultValue;
        }

        public bool GetBool(bool defaultValue, params string[] path)
        {
            if (_configDoc == null) return defaultValue;
            var element = NavigateTo(path);
            return element?.GetBoolean() ?? defaultValue;
        }

        public double[] GetDoubleArray(params string[] path)
        {
            if (_configDoc == null) return Array.Empty<double>();
            var element = NavigateTo(path);
            if (element == null) return Array.Empty<double>();
            var list = new List<double>();
            foreach (var item in element.Value.EnumerateArray())
            {
                list.Add(item.GetDouble());
            }
            return list.ToArray();
        }

        private JsonElement? NavigateTo(string[] path)
        {
            if (_configDoc == null || path.Length == 0) return null;

            JsonElement current = _configDoc.RootElement;
            foreach (var segment in path)
            {
                if (!current.TryGetProperty(segment, out current))
                    return null;
            }
            return current;
        }

        public CodeRLConfig CreateCodeRLConfig()
        {
            return new CodeRLConfig
            {
                Pipeline = new PipelineConfig
                {
                    Name = GetString("CodeRL", "Pipeline", "Name") ?? "TestPipeline",
                    EnableLLMNavigation = GetBool(true, "CodeRL", "Pipeline", "EnableLLMNavigation"),
                    EnableGraphReward = GetBool(true, "CodeRL", "Pipeline", "EnableGraphReward")
                },
                Weights = new WeightConfig
                {
                    CodebaseWeight = GetDouble(0.7, "CodeRL", "Weights", "CodebaseWeight"),
                    TemplateWeight = GetDouble(0.3, "CodeRL", "Weights", "TemplateWeight"),
                    Temperature = GetDouble(1.0, "CodeRL", "Weights", "Temperature"),
                    ExplorationRate = GetDouble(0.1, "CodeRL", "Weights", "ExplorationRate"),
                    ConfidenceThreshold = GetDouble(0.3, "CodeRL", "Weights", "ConfidenceThreshold"),
                    GraphStructureWeight = GetDouble(0.20, "CodeRL", "Weights", "GraphStructureWeight"),
                    NavigationQualityWeight = GetDouble(0.15, "CodeRL", "Weights", "NavigationQualityWeight"),
                    BaseRewardWeight = GetDouble(0.40, "CodeRL", "Weights", "BaseRewardWeight"),
                    PlaceholderIntegrityWeight = GetDouble(0.10, "CodeRL", "Weights", "PlaceholderIntegrityWeight")
                },
                LLMNavigation = new LLMNavigationConfig
                {
                    Enabled = GetBool(true, "CodeRL", "LLMNavigation", "Enabled"),
                    ConfidenceThreshold = GetDouble(0.35, "CodeRL", "LLMNavigation", "ConfidenceThreshold"),
                    MaxNavSteps = GetInt(20, "CodeRL", "LLMNavigation", "MaxNavSteps")
                },
                Skeletons = new SkeletonConfig
                {
                    Enabled = new List<string>(),
                    Disabled = new List<string>()
                },
                Training = new TrainingConfig
                {
                    MaxEpisodes = GetInt(100, "CodeRL", "Training", "MaxEpisodes"),
                    MaxStepsPerEpisode = GetInt(50, "CodeRL", "Training", "MaxStepsPerEpisode"),
                    ConvergenceThreshold = GetDouble(0.01, "CodeRL", "Training", "ConvergenceThreshold"),
                    ConvergenceWindow = GetInt(10, "CodeRL", "Training", "ConvergenceWindow"),
                    CodebasePath = GetString("CodeRL", "Training", "CodebasePath") ?? "",
                    TemplateCode = GetString("CodeRL", "Training", "TemplateCode") ?? "public void Test() { Initialize(); Process(1); Finalize(); }",
                    TestCode = GetString("CodeRL", "Training", "TestCode") ?? ""
                }
            };
        }

        public CodeRLContext CreateCodeRLContext()
        {
            var context = new CodeRLContext("test-parameterized")
            {
                CodebasePath = GetString("CodeRL", "Training", "CodebasePath") ?? Directory.GetCurrentDirectory(),
                TemplateCode = GetString("CodeRL", "Training", "TemplateCode") ?? "public void Test() { Initialize(); Process(1); Finalize(); }",
                TestCode = GetString("CodeRL", "Training", "TestCode") ?? "",
                FunctionSpace = TestDataFactory.CreateBasicFunctionSpace(),
                GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot(),
                MaxStepsPerEpisode = GetInt(50, "CodeRL", "Training", "MaxStepsPerEpisode"),
                ConvergenceThreshold = GetDouble(0.01, "CodeRL", "Training", "ConvergenceThreshold"),
                ConvergenceWindow = GetInt(10, "CodeRL", "Training", "ConvergenceWindow"),
                Weights = new WeightConfig
                {
                    CodebaseWeight = GetDouble(0.7, "CodeRL", "Weights", "CodebaseWeight"),
                    TemplateWeight = GetDouble(0.3, "CodeRL", "Weights", "TemplateWeight"),
                    Temperature = GetDouble(1.0, "CodeRL", "Weights", "Temperature"),
                    ExplorationRate = GetDouble(0.1, "CodeRL", "Weights", "ExplorationRate"),
                    ConfidenceThreshold = GetDouble(0.3, "CodeRL", "Weights", "ConfidenceThreshold"),
                    GraphStructureWeight = GetDouble(0.20, "CodeRL", "Weights", "GraphStructureWeight"),
                    NavigationQualityWeight = GetDouble(0.15, "CodeRL", "Weights", "NavigationQualityWeight"),
                    BaseRewardWeight = GetDouble(0.40, "CodeRL", "Weights", "BaseRewardWeight"),
                    PlaceholderIntegrityWeight = GetDouble(0.10, "CodeRL", "Weights", "PlaceholderIntegrityWeight")
                }
            };
            return context;
        }

        public CodeRLContext CreateContextWithTokens()
        {
            var context = CreateCodeRLContext();
            context.Tokenizer = new CodeTokenizer(context.FunctionSpace!);
            context.GeneratedTokens = context.Tokenizer.Encode(
                new List<string> { "Initialize", "Process", "Finalize" });
            return context;
        }

        public CodeRLContext CreateContextWithGeneratedCode()
        {
            var context = CreateContextWithTokens();
            context.GeneratedCode = "public void Test() { Initialize(); Process(1); Finalize(); }";
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            return context;
        }

        public CodeRLContext CreateContextWithRewardHistory()
        {
            var context = CreateContextWithGeneratedCode();
            context.Reward = 0.85;
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.7, 0.8, 0.85 });
            context.BestReward = 0.85;
            context.BestCode = context.GeneratedCode;
            return context;
        }

        public (double[] stableRewards, double[] unstableRewards, double[] insufficientHistory,
                bool expectedConverged, bool expectedNotConverged) GetConvergenceParameters()
        {
            var stable = GetDoubleArray("CodeRL", "Convergence", "StableRewards");
            var unstable = GetDoubleArray("CodeRL", "Convergence", "UnstableRewards");
            var insufficient = GetDoubleArray("CodeRL", "Convergence", "InsufficientHistory");

            return (
                stable.Length > 0 ? stable : new[] { 0.85, 0.86, 0.85, 0.86, 0.85 },
                unstable.Length > 0 ? unstable : new[] { 0.9, 0.5, 0.8, 0.4, 0.7 },
                insufficient.Length > 0 ? insufficient : new[] { 0.5, 0.6, 0.55 },
                GetBool(true, "CodeRL", "Convergence", "ExpectedConverged"),
                GetBool(false, "CodeRL", "Convergence", "ExpectedNotConverged")
            );
        }

        public double GetHighFallbackRewardThreshold()
        {
            return GetDouble(0.5, "CodeRL", "Reward", "HighFallbackRewardThreshold");
        }

        public bool IsPlaceholderEnabled()
        {
            return GetBool(true, "CodeRL", "Test", "EnablePlaceholder");
        }

        public int GetDefaultActionCount()
        {
            return GetInt(10, "CodeRL", "Test", "DefaultActionCount");
        }

        public int GetVectorDimension()
        {
            return GetInt(50, "CodeRL", "Test", "VectorDimension");
        }

        public void Dispose()
        {
            _configDoc?.Dispose();
            _context.Dispose();
        }
    }
}
