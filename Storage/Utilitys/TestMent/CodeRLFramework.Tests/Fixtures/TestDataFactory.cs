
using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.Pipeline.Placeholder;
using CodeRLFramework.RL;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Snapshots;

namespace CodeRLFramework.Tests.Fixtures
{
    public static class TestDataFactory
    {
        #region 函数向量空间

        public static FunctionVectorSpace CreateBasicFunctionSpace()
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
                        SourceWeight = 1.0,
                        Source = "test"
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
                        SourceWeight = 1.0,
                        Source = "test"
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
                        SourceWeight = 1.0,
                        Source = "test"
                    }
                }
            };
            return space;
        }

        public static FunctionVectorSpace CreateComplexFunctionSpace()
        {
            var space = new FunctionVectorSpace
            {
                Source = "test-complex",
                VectorDimension = 50,
                Functions = new List<FunctionVector>()
            };

            var funcDefs = new (string name, string returnType, string[] calls)[]
            {
                ("Main", "void", new[] { "ParseInput", "ValidateData", "ProcessData", "OutputResult" }),
                ("ParseInput", "string", new[] { "ReadFile", "Tokenize" }),
                ("ValidateData", "bool", new[] { "CheckNull", "CheckRange" }),
                ("ProcessData", "int", new[] { "Transform", "Aggregate" }),
                ("OutputResult", "void", new[] { "FormatOutput", "WriteFile" }),
                ("ReadFile", "string", new[] { "OpenStream" }),
                ("Tokenize", "string[]", new[] { "SplitTokens" }),
                ("CheckNull", "bool", Array.Empty<string>()),
                ("CheckRange", "bool", Array.Empty<string>()),
                ("Transform", "int", new[] { "MapValue" }),
                ("Aggregate", "int", new[] { "SumValues" }),
                ("FormatOutput", "string", new[] { "BuildString" })
            };

            for (int i = 0; i < funcDefs.Length; i++)
            {
                var (name, returnType, calls) = funcDefs[i];
                space.Functions.Add(new FunctionVector
                {
                    Id = $"func_{i}",
                    Name = name,
                    ReturnType = returnType,
                    SourceCode = $"public {returnType} {name}() {{ }}",
                    SemanticVector = GenerateTestVector(i * 3),
                    StructuralVector = GenerateTestVector(i * 3 + 1),
                    RelationVector = GenerateTestVector(i * 3 + 2),
                    Parameters = new List<ParameterInfo>(),
                    Calls = calls.ToList(),
                    CalledBy = new List<string>(),
                    SourceWeight = 1.0,
                    Source = "test-complex"
                });
            }

            return space;
        }

        public static FunctionVectorSpace CreateEmptyFunctionSpace()
        {
            return new FunctionVectorSpace
            {
                Source = "test-empty",
                VectorDimension = 50,
                Functions = new List<FunctionVector>()
            };
        }

        #endregion

        #region 图快照

        public static GraphSnapshot CreateBasicGraphSnapshot()
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

        public static GraphSnapshot CreateComplexGraphSnapshot()
        {
            var funcDefs = new (string name, string[] calls)[]
            {
                ("Main", new[] { "ParseInput", "ValidateData", "ProcessData", "OutputResult" }),
                ("ParseInput", new[] { "ReadFile", "Tokenize" }),
                ("ValidateData", new[] { "CheckNull", "CheckRange" }),
                ("ProcessData", new[] { "Transform", "Aggregate" }),
                ("OutputResult", new[] { "FormatOutput", "WriteFile" }),
                ("ReadFile", new[] { "OpenStream" }),
                ("Tokenize", new[] { "SplitTokens" }),
                ("CheckNull", Array.Empty<string>()),
                ("CheckRange", Array.Empty<string>()),
                ("Transform", new[] { "MapValue" }),
                ("Aggregate", new[] { "SumValues" }),
                ("FormatOutput", new[] { "BuildString" })
            };

            var nodes = new List<GraphNode>();
            var edges = new List<GraphEdge>();
            int nodeIdx = 0, edgeIdx = 0;

            foreach (var (name, calls) in funcDefs)
            {
                nodes.Add(new GraphNode
                {
                    Id = $"n{nodeIdx}",
                    Name = name,
                    NodeType = "Function"
                });
                nodeIdx++;
            }

            foreach (var (name, calls) in funcDefs)
            {
                foreach (var callee in calls)
                {
                    edges.Add(new GraphEdge
                    {
                        Id = $"e{edgeIdx}",
                        SourceId = name,
                        TargetId = callee,
                        EdgeType = "Call"
                    });
                    edgeIdx++;
                }
            }

            return new GraphSnapshot
            {
                SnapshotId = "test-complex-snapshot",
                Name = "ComplexTestGraph",
                CreatedAt = DateTime.UtcNow,
                Nodes = nodes,
                Edges = edges,
                NodeCount = nodes.Count,
                EdgeCount = edges.Count
            };
        }

        #endregion

        #region CodeRLContext

        public static CodeRLContext CreateBasicContext()
        {
            var context = new CodeRLContext("test-basic")
            {
                CodebasePath = Directory.GetCurrentDirectory(),
                TemplateCode = "public void Test() { Initialize(); Process(1); Finalize(); }",
                TestCode = "",
                FunctionSpace = CreateBasicFunctionSpace(),
                GraphSnapshot = CreateBasicGraphSnapshot(),
                MaxStepsPerEpisode = 50,
                ConvergenceThreshold = 0.01,
                ConvergenceWindow = 10,
                Weights = new WeightConfig
                {
                    CodebaseWeight = 0.7,
                    TemplateWeight = 0.3,
                    Temperature = 1.0,
                    ExplorationRate = 0.1,
                    ConfidenceThreshold = 0.3,
                    GraphStructureWeight = 0.20,
                    NavigationQualityWeight = 0.15,
                    BaseRewardWeight = 0.40,
                    PlaceholderIntegrityWeight = 0.10
                }
            };
            return context;
        }

        public static CodeRLContext CreateContextWithTokens()
        {
            var context = CreateBasicContext();
            context.Tokenizer = new CodeTokenizer(context.FunctionSpace!);
            context.GeneratedTokens = context.Tokenizer.Encode(
                new List<string> { "Initialize", "Process", "Finalize" });
            return context;
        }

        public static CodeRLContext CreateContextWithGeneratedCode()
        {
            var context = CreateContextWithTokens();
            context.GeneratedCode = "public void Test() { Initialize(); Process(1); Finalize(); }";
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            return context;
        }

        public static CodeRLContext CreateContextWithRewardHistory()
        {
            var context = CreateContextWithGeneratedCode();
            context.Reward = 0.85;
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.7, 0.8, 0.85 });
            context.BestReward = 0.85;
            context.BestCode = context.GeneratedCode;
            return context;
        }

        #endregion

        #region 导航上下文

        public static GraphNavigationContext CreateBasicNavContext()
        {
            var navContext = new GraphNavigationContext("test-nav")
            {
                StartNode = "Initialize",
                CurrentNode = "Finalize",
                MaxSteps = 50,
                ConfidenceThreshold = 0.3
            };

            navContext.RecordStep("Initialize", "Process",
                new List<string> { "Process", "Finalize" }, 0.9);
            navContext.RecordStep("Process", "Finalize",
                new List<string> { "Finalize" }, 0.85);
            navContext.IsCompleted = true;

            return navContext;
        }

        public static GraphNavigationContext CreateHighFallbackNavContext()
        {
            var navContext = new GraphNavigationContext("test-fallback")
            {
                StartNode = "Start",
                MaxSteps = 50,
                ConfidenceThreshold = 0.3
            };

            navContext.RecordStep("Start", "A",
                new List<string> { "A", "B" }, 0.1, isFallback: true);
            navContext.RecordStep("A", "B",
                new List<string> { "B" }, 0.2, isFallback: true);
            navContext.RecordStep("B", "C",
                new List<string> { "C" }, 0.15, isFallback: true);
            navContext.IsCompleted = true;

            return navContext;
        }

        public static GraphNavigationContext CreateEmptyNavContext()
        {
            return new GraphNavigationContext("test-empty")
            {
                StartNode = null,
                CurrentNode = null,
                MaxSteps = 50
            };
        }

        #endregion

        #region 配置

        public static CodeRLConfig CreateDefaultConfig()
        {
            return CodeRLConfig.GetDefault();
        }

        public static CodeRLConfig CreateCustomConfig(
            int maxEpisodes = 50,
            int maxStepsPerEpisode = 30,
            double temperature = 0.8,
            double explorationRate = 0.15)
        {
            return new CodeRLConfig
            {
                Pipeline = new PipelineConfig
                {
                    Name = "Custom-Training",
                    EnableLLMNavigation = true,
                    EnableGraphReward = true
                },
                Weights = new WeightConfig
                {
                    CodebaseWeight = 0.6,
                    TemplateWeight = 0.4,
                    Temperature = temperature,
                    ExplorationRate = explorationRate,
                    ConfidenceThreshold = 0.35,
                    GraphStructureWeight = 0.25,
                    NavigationQualityWeight = 0.15,
                    BaseRewardWeight = 0.35,
                    PlaceholderIntegrityWeight = 0.10
                },
                LLMNavigation = new LLMNavigationConfig
                {
                    Enabled = true,
                    ConfidenceThreshold = 0.35,
                    MaxNavSteps = 20
                },
                Skeletons = new SkeletonConfig
                {
                    Enabled = new List<string>(),
                    Disabled = new List<string>()
                },
                Training = new TrainingConfig
                {
                    MaxEpisodes = maxEpisodes,
                    MaxStepsPerEpisode = maxStepsPerEpisode,
                    ConvergenceThreshold = 0.01,
                    ConvergenceWindow = 10,
                    CodebasePath = Directory.GetCurrentDirectory(),
                    TemplateCode = "public void Test() { Initialize(); Process(1); Finalize(); }",
                    TestCode = ""
                }
            };
        }

        #endregion

        #region 示例代码片段

        public static class SampleCodeSnippets
        {
            public const string Simple = @"
public class TestClass
{
    public void Initialize() { }
    public int Process(int x) { return x; }
    public void Finalize() { }
}";

            public const string WithCalls = @"
public class Service
{
    public void Run()
    {
        var data = FetchData();
        var result = Transform(data);
        SaveResult(result);
    }

    public string FetchData() { return ""data""; }
    public string Transform(string input) { return input.ToUpper(); }
    public void SaveResult(string result) { }
}";

            public const string Complex = @"
public class Calculator
{
    public int Compute(int x, int y)
    {
        if (x < 0) return 0;
        var sum = Add(x, y);
        var diff = Subtract(x, y);
        return sum * diff;
    }

    public int Add(int a, int b) { return a + b; }
    public int Subtract(int a, int b) { return a - b; }
    public int Multiply(int a, int b) { return a * b; }
}";

            public const string WithPlaceholder = @"
public class GeneratedClass
{
    public void Execute()
    {
        @input:template
        @node:Initialize
        @prevstep
    }
}";

            public const string Empty = "";

            public const string Template = "public void Test() { Initialize(); Process(1); Finalize(); }";
        }

        #endregion

        #region 辅助方法

        public static double[] GenerateTestVector(int seed)
        {
            var rng = new Random(seed);
            var vector = new double[50];
            for (int i = 0; i < vector.Length; i++)
                vector[i] = rng.NextDouble();
            return vector;
        }

        public static SkeletonContext CreateSkeletonContext()
        {
            return new SkeletonContext();
        }

        #endregion
    }
}
