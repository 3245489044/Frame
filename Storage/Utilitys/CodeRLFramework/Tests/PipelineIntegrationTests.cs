
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
using WorkFlowMethod.CentralPipeline;
using WorkFlowMethod.GraphFramework.Snapshots;
using Xunit;

namespace CodeRLFramework.Tests
{
    public class PipelineIntegrationTests
    {
        #region 辅助方法

        private static FunctionVectorSpace CreateTestFunctionSpace()
        {
            var space = new FunctionVectorSpace
            {
                Source = "test",
                VectorDimension = 50,
                Functions = new List<FunctionVector>
                {
                    new()
                    {
                        Id = "func_1",
                        Name = "Initialize",
                        ReturnType = "void",
                        SourceCode = "public void Initialize() { }",
                        SemanticVector = GenerateTestVector(0),
                        StructuralVector = GenerateTestVector(1),
                        RelationVector = GenerateTestVector(2),
                        Parameters = new List<ParameterInfo>(),
                        Calls = new List<string> { "Process" },
                        CalledBy = new List<string>(),
                        SourceWeight = 1.0
                    },
                    new()
                    {
                        Id = "func_2",
                        Name = "Process",
                        ReturnType = "int",
                        SourceCode = "public int Process(int x) { return x; }",
                        SemanticVector = GenerateTestVector(3),
                        StructuralVector = GenerateTestVector(4),
                        RelationVector = GenerateTestVector(5),
                        Parameters = new List<ParameterInfo>
                        {
                            new() { Name = "x", Type = "int" }
                        },
                        Calls = new List<string> { "Finalize" },
                        CalledBy = new List<string> { "Initialize" },
                        SourceWeight = 1.0
                    },
                    new()
                    {
                        Id = "func_3",
                        Name = "Finalize",
                        ReturnType = "void",
                        SourceCode = "public void Finalize() { }",
                        SemanticVector = GenerateTestVector(6),
                        StructuralVector = GenerateTestVector(7),
                        RelationVector = GenerateTestVector(8),
                        Parameters = new List<ParameterInfo>(),
                        Calls = new List<string>(),
                        CalledBy = new List<string> { "Process" },
                        SourceWeight = 1.0
                    }
                }
            };
            return space;
        }

        private static double[] GenerateTestVector(int seed)
        {
            var rng = new Random(seed);
            var vector = new double[50];
            for (int i = 0; i < vector.Length; i++)
                vector[i] = rng.NextDouble();
            return vector;
        }

        private static CodeRLContext CreateTestContext()
        {
            var context = new CodeRLContext("test-integration")
            {
                CodebasePath = Directory.GetCurrentDirectory(),
                TemplateCode = "public void Test() { Initialize(); Process(1); Finalize(); }",
                TestCode = "",
                FunctionSpace = CreateTestFunctionSpace(),
                MaxStepsPerEpisode = 50,
                ConvergenceThreshold = 0.01,
                ConvergenceWindow = 10,
                Weights = new WeightConfig
                {
                    CodebaseWeight = 0.7,
                    TemplateWeight = 0.3,
                    Temperature = 1.0,
                    ExplorationRate = 0.1
                }
            };
            return context;
        }

        #endregion

        #region 管道创建测试

        [Fact]
        public void CodeRLPipeline_Create_ShouldSucceed()
        {
            var pipeline = new CodeRLPipeline("TestPipeline");

            Assert.NotNull(pipeline);
            Assert.Equal("TestPipeline", pipeline.Name);
            Assert.NotNull(pipeline.InnerPipeline);
            Assert.NotNull(pipeline.Factory);
        }

        [Fact]
        public void CodeRLPipeline_CreateWithDefaultName_ShouldUseDefault()
        {
            var pipeline = new CodeRLPipeline();

            Assert.NotNull(pipeline);
            Assert.Equal("CodeRLPipeline", pipeline.Name);
        }

        [Fact]
        public void CodeRLPipelineFactory_CreateSkeletons_ShouldReturnValidInstances()
        {
            var factory = new CodeRLPipelineFactory();

            var extractSkel = factory.CreateCodeExtractSkeleton();
            var vectorizeSkel = factory.CreateVectorizeSkeleton();
            var validateSkel = factory.CreateGraphValidateSkeleton();
            var tokenizeSkel = factory.CreateTokenizeSkeleton();
            var rewardSkel = factory.CreateRewardEvalSkeleton();
            var convergeSkel = factory.CreateConvergenceCheckSkeleton();

            Assert.NotNull(extractSkel);
            Assert.NotNull(vectorizeSkel);
            Assert.NotNull(validateSkel);
            Assert.NotNull(tokenizeSkel);
            Assert.NotNull(rewardSkel);
            Assert.NotNull(convergeSkel);
            Assert.Equal("CodeExtractSkeleton", extractSkel.Name);
            Assert.Equal("VectorizeSkeleton", vectorizeSkel.Name);
            Assert.Equal("GraphValidateSkeleton", validateSkel.Name);
            Assert.Equal("TokenizeSkeleton", tokenizeSkel.Name);
            Assert.Equal("RewardEvalSkeleton", rewardSkel.Name);
            Assert.Equal("ConvergenceCheckSkeleton", convergeSkel.Name);
        }

        [Fact]
        public void CodeRLPipelineFactory_CreateTrainingPipeline_ShouldReturnComposite()
        {
            var factory = new CodeRLPipelineFactory();

            var pipeline = factory.CreateTrainingPipeline();

            Assert.NotNull(pipeline);
            Assert.Contains("→", pipeline.Name);
        }

        [Fact]
        public void CodeRLPipelineFactory_CreateSingleEpisodePipeline_ShouldReturnComposite()
        {
            var factory = new CodeRLPipelineFactory();

            var pipeline = factory.CreateSingleEpisodePipeline();

            Assert.NotNull(pipeline);
            Assert.Contains("→", pipeline.Name);
        }

        #endregion

        #region Skeleton 执行测试

        [Fact]
        public void VectorizeSkeleton_Execute_ShouldAttachVectors()
        {
            var context = CreateTestContext();
            var skeleton = new VectorizeSkeleton();
            using var skelContext = new SkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
            Assert.All(result.FunctionSpace.Functions, func =>
            {
                Assert.NotNull(func.SemanticVector);
                Assert.NotEmpty(func.SemanticVector);
                Assert.NotNull(func.StructuralVector);
                Assert.NotEmpty(func.StructuralVector);
                Assert.NotNull(func.RelationVector);
                Assert.NotEmpty(func.RelationVector);
            });
        }

        [Fact]
        public void GraphValidateSkeleton_WithoutGraphSnapshot_ShouldThrow()
        {
            var context = CreateTestContext();
            context.GraphSnapshot = null;
            var skeleton = new GraphValidateSkeleton();
            using var skelContext = new SkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void TokenizeSkeleton_Execute_ShouldEncodeTokens()
        {
            var context = CreateTestContext();
            var skeleton = new TokenizeSkeleton();
            using var skelContext = new SkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.Tokenizer);
            Assert.NotNull(result.GeneratedTokens);
            Assert.NotEmpty(result.GeneratedTokens);
        }

        [Fact]
        public void ConvergenceCheckSkeleton_NotConverged_WhenInsufficientHistory()
        {
            var context = CreateTestContext();
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.55 });
            var skeleton = new ConvergenceCheckSkeleton();
            using var skelContext = new SkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        [Fact]
        public void ConvergenceCheckSkeleton_Converged_WhenStable()
        {
            var context = CreateTestContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.85, 0.86, 0.85, 0.86, 0.85 });
            var skeleton = new ConvergenceCheckSkeleton();
            using var skelContext = new SkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged);
        }

        [Fact]
        public void ConvergenceCheckSkeleton_NotConverged_WhenUnstable()
        {
            var context = CreateTestContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.9, 0.5, 0.8, 0.4, 0.7 });
            var skeleton = new ConvergenceCheckSkeleton();
            using var skelContext = new SkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        #endregion

        #region 管道执行测试

        [Fact]
        public void CodeRLPipeline_ExecuteEpisode_ShouldIncrementEpisode()
        {
            var context = CreateTestContext();
            var pipeline = new CodeRLPipeline("TestEpisode");

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.Equal(1, result.Episode);
        }

        [Fact]
        public void CodeRLPipeline_ExecuteEpisode_MultipleTimes_ShouldIncrement()
        {
            var context = CreateTestContext();
            var pipeline = new CodeRLPipeline("TestMultiEpisode");

            var result1 = pipeline.ExecuteEpisode(context);
            var result2 = pipeline.ExecuteEpisode(result1);
            var result3 = pipeline.ExecuteEpisode(result2);

            Assert.Equal(1, result1.Episode);
            Assert.Equal(2, result2.Episode);
            Assert.Equal(3, result3.Episode);
        }

        [Fact]
        public void CodeRLPipeline_Train_ShouldReturnResult()
        {
            var context = CreateTestContext();
            var pipeline = new CodeRLPipeline("TestTrain");

            var result = pipeline.Train(
                codebasePath: Directory.GetCurrentDirectory(),
                templateCode: "public void Test() { Initialize(); Process(1); Finalize(); }",
                maxEpisodes: 5);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.InRange(result.TotalEpisodes, 1, 5);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public async Task CodeRLPipeline_TrainAsync_ShouldReturnResult()
        {
            var context = CreateTestContext();
            var pipeline = new CodeRLPipeline("TestTrainAsync");

            var result = await pipeline.TrainAsync(
                codebasePath: Directory.GetCurrentDirectory(),
                templateCode: "public void Test() { Initialize(); Process(1); Finalize(); }",
                maxEpisodes: 5);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.InRange(result.TotalEpisodes, 1, 5);
        }

        [Fact]
        public void CodeRLPipeline_Train_WithProgressCallback_ShouldInvoke()
        {
            var pipeline = new CodeRLPipeline("TestProgress");
            int callbackCount = 0;

            var result = pipeline.Train(
                codebasePath: Directory.GetCurrentDirectory(),
                templateCode: "public void Test() { Initialize(); Process(1); Finalize(); }",
                maxEpisodes: 3,
                onProgress: progress =>
                {
                    callbackCount++;
                    Assert.InRange(progress.Episode, 1, 3);
                });

            Assert.True(result.IsSuccess);
            Assert.InRange(callbackCount, 1, 3);
        }

        #endregion

        #region WorkflowContext 适配器测试

        [Fact]
        public void CodeRLAdapterSkeleton_Execute_ShouldProcessWorkflowContext()
        {
            var factory = new CodeRLPipelineFactory();
            var adapter = new CodeRLAdapterSkeleton(factory);
            var workflowContext = new WorkflowContext("test-adapter");
            using var skelContext = new SkeletonContext();

            workflowContext.SetData("CodeRL.CodebasePath", Directory.GetCurrentDirectory());
            workflowContext.SetData("CodeRL.TemplateCode",
                "public void Test() { Initialize(); Process(1); Finalize(); }");
            workflowContext.SetData("CodeRL.TestCode", "");
            workflowContext.SetData("CodeRL.MaxEpisodes", 5);
            workflowContext.SetData("CodeRL.MaxStepsPerEpisode", 50);
            workflowContext.SetData("CodeRL.ConvergenceThreshold", 0.01);
            workflowContext.SetData("CodeRL.ConvergenceWindow", 10);

            var result = adapter.Execute(workflowContext, skelContext);

            Assert.NotNull(result);
            Assert.True(result.ContainsKey("CodeRL.Episode"));
            Assert.True(result.ContainsKey("CodeRL.Reward"));
            Assert.True(result.ContainsKey("CodeRL.BestReward"));
            Assert.True(result.ContainsKey("CodeRL.IsConverged"));
            Assert.True(result.ContainsKey("CodeRL.GeneratedCode"));
        }

        [Fact]
        public void CodeRLAdapterSkeleton_Execute_ShouldStoreGraphSnapshot()
        {
            var factory = new CodeRLPipelineFactory();
            var adapter = new CodeRLAdapterSkeleton(factory);
            var workflowContext = new WorkflowContext("test-snapshot");
            using var skelContext = new SkeletonContext();

            workflowContext.SetData("CodeRL.CodebasePath", Directory.GetCurrentDirectory());
            workflowContext.SetData("CodeRL.TemplateCode",
                "public void Test() { Initialize(); Process(1); Finalize(); }");
            workflowContext.SetData("CodeRL.TestCode", "");
            workflowContext.SetData("CodeRL.MaxEpisodes", 1);
            workflowContext.SetData("CodeRL.MaxStepsPerEpisode", 50);
            workflowContext.SetData("CodeRL.ConvergenceThreshold", 0.01);
            workflowContext.SetData("CodeRL.ConvergenceWindow", 10);

            var result = adapter.Execute(workflowContext, skelContext);

            Assert.NotNull(result);
            var snapshot = result.GetGraphSnapshot();
        }

        [Fact]
        public void CodeRLPipeline_ExecuteWorkflow_ShouldWork()
        {
            var pipeline = new CodeRLPipeline("TestWorkflow");
            var workflowContext = new WorkflowContext("test-pipeline-workflow");

            workflowContext.SetData("CodeRL.CodebasePath", Directory.GetCurrentDirectory());
            workflowContext.SetData("CodeRL.TemplateCode",
                "public void Test() { Initialize(); Process(1); Finalize(); }");
            workflowContext.SetData("CodeRL.TestCode", "");
            workflowContext.SetData("CodeRL.MaxEpisodes", 3);
            workflowContext.SetData("CodeRL.MaxStepsPerEpisode", 50);
            workflowContext.SetData("CodeRL.ConvergenceThreshold", 0.01);
            workflowContext.SetData("CodeRL.ConvergenceWindow", 10);

            var result = pipeline.ExecuteWorkflow(workflowContext);

            Assert.NotNull(result);
        }

        #endregion

        #region 管道描述测试

        [Fact]
        public void CodeRLPipeline_GetDescription_ShouldReturnNonEmpty()
        {
            var pipeline = new CodeRLPipeline("TestDesc");

            var description = pipeline.GetDescription();

            Assert.NotNull(description);
            Assert.NotEmpty(description);
        }

        [Fact]
        public void CodeRLPipeline_Status_ShouldBeInitialized()
        {
            var pipeline = new CodeRLPipeline("TestStatus");

            var status = pipeline.Status;

            Assert.NotNull(status);
        }

        #endregion

        #region 上下文测试

        [Fact]
        public void CodeRLContext_Reset_ShouldClearTrainingState()
        {
            var context = CreateTestContext();
            context.Episode = 10;
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.7 });
            context.BestReward = 0.7;
            context.BestCode = "test code";
            context.IsConverged = true;
            context.GeneratedTokens = new[] { 1, 2, 3 };
            context.GeneratedCode = "generated";

            context.Reset();

            Assert.Equal(0, context.Episode);
            Assert.Empty(context.RewardHistory);
            Assert.Equal(double.MinValue, context.BestReward);
            Assert.Null(context.BestCode);
            Assert.False(context.IsConverged);
            Assert.Null(context.GeneratedTokens);
            Assert.Null(context.GeneratedCode);
            Assert.Equal(0, context.Reward);
        }

        [Fact]
        public void CodeRLContext_SetAndGetData_ShouldWork()
        {
            var context = CreateTestContext();

            context.SetData("TestKey", 42);
            var value = context.GetData<int>("TestKey");

            Assert.Equal(42, value);
        }

        [Fact]
        public void CodeRLContext_WorkflowCtx_ShouldBeAccessible()
        {
            var context = CreateTestContext();

            Assert.NotNull(context.WorkflowCtx);
            Assert.Contains("test-integration", context.WorkflowCtx.ContextId);
        }

        #endregion

        #region 图结构奖励测试

        [Fact]
        public void CalculateGraphStructureReward_WithValidCode_ShouldReturnPositive()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = CreateTestGraphSnapshot();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var reward = rewardFunc.CalculateGraphStructureReward(code, graphSnapshot);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward >= 0, $"图结构奖励应为非负值，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateGraphStructureReward_WithInvalidCode_ShouldReturnLower()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = CreateTestGraphSnapshot();
            var code = "public void Test() { NonExistentFunc(); AnotherBadCall(); }";

            var reward = rewardFunc.CalculateGraphStructureReward(code, graphSnapshot);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward <= 0.3, $"无效引用的图结构奖励应较低，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateGraphStructureReward_WithEmptyCode_ShouldReturnNegative()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = CreateTestGraphSnapshot();

            var reward = rewardFunc.CalculateGraphStructureReward("", graphSnapshot);

            Assert.Equal(-0.5, reward);
        }

        [Fact]
        public void CalculateGraphStructureReward_WithNullGraph_ShouldReturnZero()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.CalculateGraphStructureReward("public void Test() { }", null!);

            Assert.Equal(0, reward);
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithGoodPath_ShouldReturnPositive()
        {
            var rewardFunc = new RewardFunction();
            var navContext = new GraphNavigationContext("test-nav")
            {
                StartNode = "Initialize",
                CurrentNode = "Finalize"
            };

            navContext.RecordStep("Initialize", "Process", new List<string> { "Process", "Finalize" }, 0.9);
            navContext.RecordStep("Process", "Finalize", new List<string> { "Finalize" }, 0.85);
            navContext.IsCompleted = true;

            var reward = rewardFunc.CalculateNavigationQualityReward(navContext);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward >= 0, $"良好导航路径的奖励应为非负值，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithHighFallback_ShouldReturnLower()
        {
            var rewardFunc = new RewardFunction();
            var navContext = new GraphNavigationContext("test-fallback")
            {
                StartNode = "Start"
            };

            navContext.RecordStep("Start", "A", new List<string> { "A", "B" }, 0.1, isFallback: true);
            navContext.RecordStep("A", "B", new List<string> { "B" }, 0.2, isFallback: true);
            navContext.RecordStep("B", "C", new List<string> { "C" }, 0.15, isFallback: true);
            navContext.IsCompleted = true;

            var reward = rewardFunc.CalculateNavigationQualityReward(navContext);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward <= 0.5, $"高回退率的导航奖励应较低，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithEmptyContext_ShouldReturnZero()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.CalculateNavigationQualityReward(null!);

            Assert.Equal(0, reward);
        }

        [Fact]
        public void Calculate_WithGraphSnapshot_ShouldIncludeGraphReward()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = CreateTestGraphSnapshot();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var rewardWithGraph = rewardFunc.Calculate(code, "", graphSnapshot, null);
            var rewardWithoutGraph = rewardFunc.Calculate(code, "", null, null);

            Assert.NotEqual(rewardWithGraph, rewardWithoutGraph);
        }

        [Fact]
        public void Calculate_WithNavContext_ShouldIncludeNavReward()
        {
            var rewardFunc = new RewardFunction();
            var navContext = new GraphNavigationContext("test-nav-calc");
            navContext.RecordStep("Initialize", "Process", new List<string> { "Process" }, 0.9);
            navContext.RecordStep("Process", "Finalize", new List<string> { "Finalize" }, 0.85);
            navContext.IsCompleted = true;

            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var rewardWithNav = rewardFunc.Calculate(code, "", null, navContext);
            var rewardWithoutNav = rewardFunc.Calculate(code, "", null, null);

            Assert.NotEqual(rewardWithNav, rewardWithoutNav);
        }

        private static GraphSnapshot CreateTestGraphSnapshot()
        {
            var nodes = new List<GraphNode>
            {
                new() { Id = "n1", Name = "Initialize", NodeType = "Function" },
                new() { Id = "n2", Name = "Process", NodeType = "Function" },
                new() { Id = "n3", Name = "Finalize", NodeType = "Function" }
            };

            var edges = new List<GraphEdge>
            {
                new() { Id = "e1", SourceId = "Initialize", TargetId = "Process", EdgeType = "Call" },
                new() { Id = "e2", SourceId = "Process", TargetId = "Finalize", EdgeType = "Call" }
            };

            return new GraphSnapshot
            {
                SnapshotId = "test-snapshot",
                Name = "TestGraph",
                CreatedAt = DateTime.UtcNow,
                Nodes = nodes,
                Edges = edges,
                NodeCount = nodes.Count,
                EdgeCount = edges.Count
            };
        }

        #endregion

        #region 边界情况测试

        [Fact]
        public void CodeRLPipeline_Train_WithEmptyCodebase_ShouldHandleGracefully()
        {
            var pipeline = new CodeRLPipeline("TestEmpty");

            var result = pipeline.Train(
                codebasePath: Path.GetTempPath(),
                templateCode: "public void Test() { }",
                maxEpisodes: 1);

            Assert.NotNull(result);
        }

        [Fact]
        public void CodeRLPipeline_Train_WithMaxEpisodesZero_ShouldReturnImmediately()
        {
            var pipeline = new CodeRLPipeline("TestZero");

            var result = pipeline.Train(
                codebasePath: Directory.GetCurrentDirectory(),
                templateCode: "public void Test() { }",
                maxEpisodes: 0);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void SkeletonContext_CancellationToken_ShouldDefaultToNone()
        {
            using var context = new SkeletonContext();

            Assert.False(context.IsCancellationRequested);
        }

        [Fact]
        public void SkeletonContext_Items_ShouldBeMutable()
        {
            using var context = new SkeletonContext();

            context.Items["TestKey"] = "TestValue";

            Assert.Equal("TestValue", context.Items["TestKey"]);
        }

        #endregion
    }
}
