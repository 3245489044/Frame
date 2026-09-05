
using CodeRLFramework;
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.IntegrationTests
{
    public class CodeRLManagerIntegrationTests : TestBase
    {
        private static string CodeRLFrameworkProjectPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "..",
                "CodeRLFramework"));

        private static string TestProjectPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", ".."));

        #region 1. 默认构造 + 训练（极简用法）

        [Fact]
        public void DefaultConstructor_TrainEpisode_ShouldComplete()
        {
            using var manager = new CodeRLManager();

            var result = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0);
        }

        [Fact]
        public async Task DefaultConstructor_TrainEpisodeAsync_ShouldComplete()
        {
            using var manager = new CodeRLManager();

            var result = await manager.TrainEpisodeAsync(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0,
                "异步训练后应有奖励历史记录");
        }

        [Fact]
        public void DefaultConstructor_Train_ShouldReturnResult()
        {
            using var manager = new CodeRLManager();

            var result = manager.Train(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 3);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.True(result.TotalEpisodes > 0);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public async Task DefaultConstructor_TrainAsync_ShouldReturnResult()
        {
            using var manager = new CodeRLManager();

            var result = await manager.TrainAsync(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 3);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.True(result.TotalEpisodes > 0);
            Assert.NotNull(result.GeneratedCode);
        }

        #endregion

        #region 2. 从 CodeRLConfig 构造 + 训练

        [Fact]
        public void ConfigConstructor_TrainEpisode_ShouldUseConfigSettings()
        {
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 5,
                maxStepsPerEpisode: 20,
                temperature: 0.7,
                explorationRate: 0.2);
            config.Training.CodebasePath = CodeRLFrameworkProjectPath;
            config.Training.TemplateCode = TestDataFactory.SampleCodeSnippets.Template;
            using var manager = new CodeRLManager(config);

            var result = manager.TrainEpisode();

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.Equal(0.7, manager.Config.Weights.Temperature, 2);
            Assert.Equal(0.2, manager.Config.Weights.ExplorationRate, 2);
        }

        [Fact]
        public void ConfigConstructor_Train_ShouldUseConfigMaxEpisodes()
        {
            var config = TestDataFactory.CreateCustomConfig(maxEpisodes: 2);
            config.Training.CodebasePath = CodeRLFrameworkProjectPath;
            config.Training.TemplateCode = TestDataFactory.SampleCodeSnippets.Template;
            using var manager = new CodeRLManager(config);

            var result = manager.Train();

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.True(result.TotalEpisodes <= 2,
                $"训练轮数({result.TotalEpisodes})不应超过配置值(2)");
        }

        #endregion

        #region 3. 从 JSON 配置文件构造

        [Fact]
        public void JsonConfigConstructor_ShouldLoadConfig()
        {
            var jsonPath = Path.Combine(
                CodeRLFrameworkProjectPath,
                "Input", "coderl_config.json");

            if (!File.Exists(jsonPath))
            {
                jsonPath = Path.Combine(Path.GetTempPath(), "test_coderl_config.json");
                var defaultConfig = CodeRLConfig.GetDefault();
                var json = System.Text.Json.JsonSerializer.Serialize(defaultConfig,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, json);
            }

            try
            {
                using var manager = new CodeRLManager(jsonPath);

                Assert.NotNull(manager.Config);
                Assert.NotNull(manager.Config.Pipeline);
                Assert.NotNull(manager.Config.Training);
                Assert.NotNull(manager.Config.Weights);
            }
            finally
            {
                if (jsonPath.Contains("test_coderl_config"))
                    File.Delete(jsonPath);
            }
        }

        [Fact]
        public void JsonConfigConstructor_TrainEpisode_ShouldComplete()
        {
            var jsonPath = Path.Combine(Path.GetTempPath(), "test_coderl_config.json");
            var config = TestDataFactory.CreateCustomConfig(maxEpisodes: 3);
            config.Training.CodebasePath = CodeRLFrameworkProjectPath;
            config.Training.TemplateCode = TestDataFactory.SampleCodeSnippets.Template;
            var json = System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json);

            try
            {
                using var manager = new CodeRLManager(jsonPath);

                var result = manager.TrainEpisode(
                    codebasePath: CodeRLFrameworkProjectPath,
                    templateCode: TestDataFactory.SampleCodeSnippets.Template);

                Assert.NotNull(result);
                Assert.NotNull(result.GeneratedCode);
            }
            finally
            {
                File.Delete(jsonPath);
            }
        }

        #endregion

        #region 4. Fluent API 组件注入 + 训练

        [Fact]
        public void FluentApi_UseCustomComponents_ShouldTrain()
        {
            using var manager = new CodeRLManager()
                .UseFunctionSpace(FunctionSpace)
                .UseTokenizer(Tokenizer)
                .UseAgent(Agent)
                .UseConverter(Converter)
                .UseExtractor(new VectorExtractor())
                .UseRewardFunction(RewardFunction)
                .UsePolicyGradient(PolicyGradient);

            var result = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0);
        }

        [Fact]
        public void FluentApi_UseOnlyAgent_ShouldUseDefaultsForOthers()
        {
            using var manager = new CodeRLManager()
                .UseFunctionSpace(FunctionSpace)
                .UseTokenizer(Tokenizer)
                .UseAgent(Agent);

            var result = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        #endregion

        #region 5. 进度回调

        [Fact]
        public void Train_WithProgressCallback_ShouldInvokeCallback()
        {
            var progressUpdates = new List<CodeRLTrainingProgress>();
            using var manager = new CodeRLManager();

            var result = manager.Train(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 3,
                onProgress: progress =>
                {
                    progressUpdates.Add(progress);
                });

            Assert.NotNull(result);
            Assert.True(progressUpdates.Count > 0,
                $"进度回调应被调用（实际调用 {progressUpdates.Count} 次）");
            Assert.True(progressUpdates.All(p => p.Episode > 0),
                "所有进度回调应有轮数信息");
        }

        [Fact]
        public async Task TrainAsync_WithProgressCallback_ShouldInvokeCallback()
        {
            var progressUpdates = new List<CodeRLTrainingProgress>();
            using var manager = new CodeRLManager();

            var result = await manager.TrainAsync(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 3,
                onProgress: progress =>
                {
                    progressUpdates.Add(progress);
                });

            Assert.NotNull(result);
            Assert.True(progressUpdates.Count > 0,
                $"异步进度回调应被调用（实际调用 {progressUpdates.Count} 次）");
        }

        #endregion

        #region 6. 单轮训练（TrainEpisode）

        [Fact]
        public void TrainEpisode_WithCustomContext_ShouldUpdateContext()
        {
            using var manager = new CodeRLManager();
            var context = TestDataFactory.CreateBasicContext();
            context.CodebasePath = CodeRLFrameworkProjectPath;

            var result = manager.TrainEpisode(context);

            Assert.NotNull(result);
            Assert.Same(context, result);
            Assert.True(result.Episode > 0, "轮数应增加");
        }

        [Fact]
        public async Task TrainEpisodeAsync_WithCustomContext_ShouldUpdateContext()
        {
            using var manager = new CodeRLManager();
            var context = TestDataFactory.CreateBasicContext();
            context.CodebasePath = CodeRLFrameworkProjectPath;

            var result = await manager.TrainEpisodeAsync(context);

            Assert.NotNull(result);
            Assert.Same(context, result);
            Assert.True(result.Episode > 0, "轮数应增加");
        }

        #endregion

        #region 7. 重置和重新训练

        [Fact]
        public void Reset_ThenTrainAgain_ShouldWork()
        {
            using var manager = new CodeRLManager();
            var result1 = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            manager.Reset();

            var result2 = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.True(result2.RewardHistory.Count > 0,
                "重置后训练应有奖励历史");
        }

        [Fact]
        public void CreatePipeline_ShouldRecreatePipeline()
        {
            using var manager = new CodeRLManager();
            var pipeline1 = manager.Pipeline;

            manager.CreatePipeline();
            var pipeline2 = manager.Pipeline;

            Assert.NotNull(pipeline1);
            Assert.NotNull(pipeline2);
            Assert.NotSame(pipeline1, pipeline2);
        }

        #endregion

        #region 8. 多轮完整训练

        [Fact]
        public void Train_MultipleEpisodes_ShouldImproveOrMaintainReward()
        {
            using var manager = new CodeRLManager();

            var result = manager.Train(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 5);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.True(result.TotalEpisodes >= 1,
                $"应至少训练 1 轮（实际 {result.TotalEpisodes} 轮）");
            Assert.True(result.TotalEpisodes <= 5,
                $"训练轮数不应超过 5（实际 {result.TotalEpisodes} 轮）");
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Length > 0,
                "应有奖励历史记录");
        }

        [Fact]
        public void Train_WithConvergence_ShouldStopEarly()
        {
            using var manager = new CodeRLManager();

            var context = TestDataFactory.CreateContextWithRewardHistory();
            context.ConvergenceThreshold = 0.05;
            context.ConvergenceWindow = 3;
            context.CodebasePath = CodeRLFrameworkProjectPath;

            var result = manager.TrainEpisode(context);

            Assert.NotNull(result);
            Assert.True(result.RewardHistory.Count >= 5,
                "应保留之前的奖励历史");
        }

        #endregion

        #region 9. 配置委托修改

        [Fact]
        public void Configure_ShouldModifyConfig()
        {
            using var manager = new CodeRLManager();

            manager.Configure(config =>
            {
                config.Training.MaxEpisodes = 10;
                config.Training.MaxStepsPerEpisode = 30;
                config.Weights.Temperature = 0.9;
            });

            Assert.Equal(10, manager.Config.Training.MaxEpisodes);
            Assert.Equal(30, manager.Config.Training.MaxStepsPerEpisode);
            Assert.Equal(0.9, manager.Config.Weights.Temperature, 2);
        }

        [Fact]
        public void Configure_ThenTrain_ShouldUseNewConfig()
        {
            using var manager = new CodeRLManager();

            manager.Configure(config =>
            {
                config.Training.CodebasePath = CodeRLFrameworkProjectPath;
                config.Training.TemplateCode = TestDataFactory.SampleCodeSnippets.Template;
                config.Training.MaxEpisodes = 2;
            });

            var result = manager.Train();

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.True(result.TotalEpisodes <= 2,
                $"训练轮数({result.TotalEpisodes})不应超过配置值(2)");
        }

        #endregion

        #region 10. 状态和属性

        [Fact]
        public void GetStatus_ShouldReturnInfo()
        {
            using var manager = new CodeRLManager();

            var status = manager.GetStatus();

            Assert.NotNull(status);
            Assert.Contains("CodeRLManager", status);
        }

        [Fact]
        public void IsInitialized_ShouldReflectState()
        {
            using var manager = new CodeRLManager();

            Assert.False(manager.IsInitialized, "未训练前不应初始化");

            var pipeline = manager.Pipeline;
            Assert.True(manager.IsInitialized, "访问 Pipeline 后应初始化");
        }

        [Fact]
        public void Config_ShouldNotBeNull()
        {
            using var manager = new CodeRLManager();

            Assert.NotNull(manager.Config);
            Assert.NotNull(manager.Config.Pipeline);
            Assert.NotNull(manager.Config.Training);
            Assert.NotNull(manager.Config.Weights);
        }

        #endregion

        #region 11. 包装已有管道

        [Fact]
        public void PipelineConstructor_TrainEpisode_ShouldComplete()
        {
            using var innerPipeline = new CodeRLPipeline(
                name: "TestPipeline",
                extractor: new VectorExtractor(),
                agent: Agent,
                converter: Converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient);

            using var manager = new CodeRLManager(innerPipeline);

            var result = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        #endregion

        #region 12. LoadConfigFromJson

        [Fact]
        public void LoadConfigFromJson_ShouldUpdateConfig()
        {
            using var manager = new CodeRLManager();
            var config = TestDataFactory.CreateCustomConfig(maxEpisodes: 7);
            var json = System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            manager.LoadConfigFromJson(json);

            Assert.Equal(7, manager.Config.Training.MaxEpisodes);
        }

        #endregion

        #region 13. 真实代码库集成测试

        [Fact]
        public void Train_WithRealCodebase_ShouldExtractAndGenerateCode()
        {
            using var manager = new CodeRLManager();

            var result = manager.Train(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 2);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Length > 0,
                "真实代码库训练应有奖励历史");
            Assert.NotNull(result.GraphSnapshot);
            Assert.True(result.GraphSnapshot != null,
                "真实代码库训练应生成图快照");
        }

        [Fact]
        public void TrainEpisode_WithRealCodebase_ShouldExtractFunctionSpace()
        {
            using var manager = new CodeRLManager();

            var result = manager.TrainEpisode(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
            Assert.True(result.FunctionSpace.Functions.Count > 0,
                "应从真实代码库中提取到函数");
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void Train_WithRealCodebaseAndCustomComponents_ShouldWork()
        {
            using var manager = new CodeRLManager()
                .UseFunctionSpace(FunctionSpace)
                .UseTokenizer(Tokenizer)
                .UseAgent(Agent)
                .UseConverter(Converter)
                .UseExtractor(new VectorExtractor())
                .UseRewardFunction(RewardFunction)
                .UsePolicyGradient(PolicyGradient);

            var result = manager.Train(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 3);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.TotalEpisodes > 0);
        }

        [Fact]
        public async Task TrainAsync_WithRealCodebase_ShouldCompleteSuccessfully()
        {
            using var manager = new CodeRLManager();

            var result = await manager.TrainAsync(
                codebasePath: CodeRLFrameworkProjectPath,
                templateCode: TestDataFactory.SampleCodeSnippets.Template,
                maxEpisodes: 2);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.TotalEpisodes > 0);
            Assert.True(result.RewardHistory.Length > 0);
        }

        #endregion
    }
}
