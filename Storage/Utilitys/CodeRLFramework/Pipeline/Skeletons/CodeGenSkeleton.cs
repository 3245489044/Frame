
#region extern alias
extern alias AgentAlias;
#endregion

#region USINGS
using AgentAlias::Agent.AgentFlow;
using AgentAlias::Agent.AgentFlow.Config;
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Snapshots;

#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class CodeGenSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private CodeGenerationAgent? _agent;
        private readonly LLMNavSelectSkeleton _navSkeleton;
        private readonly object _lock = new();

        public CodeGenSkeleton(
            CodeGenerationAgent? agent = null,
            LLMNavSelectSkeleton? navSkeleton = null)
            : base("CodeGenSkeleton", "集成大模型导航循环的代码生成 Skeleton")
        {
            _agent = agent;
            _navSkeleton = navSkeleton ?? throw new ArgumentNullException(nameof(navSkeleton));
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.GeneratedTokens == null || input.GeneratedTokens.Length == 0)
                throw new InvalidOperationException("GeneratedTokens 未设置，请先执行 TokenizeSkeleton");

            if (input.GraphSnapshot == null && input.FunctionSpace != null)
            {
                input.GraphSnapshot = CreateDefaultGraphSnapshot(input.FunctionSpace);
            }


            EnsureAgentCompatible(input);

            PerformNavigation(input, context);

            var generatedCode = GenerateFromNavigation(input, context);

            input.GeneratedCode = generatedCode;

            context.Items["CodeGenSkeleton.Info"] =
                $"代码生成完成: 导航步数={GetNavigationStepCount(input)}, " +
                $"生成代码长度={generatedCode?.Length ?? 0}";

            return Task.FromResult(input);
        }

        private void PerformNavigation(CodeRLContext input, SkeletonContext context)
        {
            int maxNavSteps = input.MaxStepsPerEpisode;
            int navStep = 0;

            while (navStep < maxNavSteps)
            {
                var navResult = _navSkeleton.Execute(input, context);

                if (navResult.Properties.TryGetValue("GraphNavigationContext", out var navCtxObj) &&
                    navCtxObj is GraphNavigationContext navCtx)
                {
                    if (navCtx.IsCompleted || navCtx.IsDeadEnd)
                    {
                        context.Items["CodeGenSkeleton.NavEnd"] =
                            navCtx.IsCompleted ? "导航完成" : "死胡同，导航结束";
                        break;
                    }
                }

                navStep++;
            }

            context.Items["CodeGenSkeleton.NavSteps"] = navStep;
        }

        private string? GenerateFromNavigation(CodeRLContext input, SkeletonContext context)
        {
            if (_agent == null) return null;

            double temperature = input.Weights?.Temperature ?? 1.0;

            var generatedTokens = _agent!.Generate(
                input.GeneratedTokens ?? Array.Empty<int>(),
                input.MaxStepsPerEpisode,
                temperature);

            input.GeneratedTokens = generatedTokens;

            if (input.Tokenizer != null)
            {
                var decodedNames = input.Tokenizer.DecodeToNames(generatedTokens);
                input.DecodedFunctionNames = decodedNames;
                return string.Join("; ", decodedNames);
            }

            return null;
        }

        private static List<string> GetCandidatesFromSnapshot(
            WorkFlowMethod.GraphFramework.Snapshots.GraphSnapshot snapshot,
            string currentNode)
        {
            var candidates = new List<string>();
            foreach (var edge in snapshot.Edges)
            {
                if (edge.SourceId == currentNode)
                {
                    candidates.Add(edge.TargetId);
                }
            }
            return candidates;
        }

        private static int GetNavigationStepCount(CodeRLContext input)
        {
            if (input.Properties.TryGetValue("GraphNavigationContext", out var navCtxObj) &&
                navCtxObj is GraphNavigationContext navCtx)
            {
                return navCtx.CurrentStep;
            }
            return 0;
        }

        private void EnsureAgentCompatible(CodeRLContext input)
        {
            if (input.Tokenizer == null) return;

            lock (_lock)
            {
                if (_agent == null || _agent.Tokenizer.VocabSize != input.Tokenizer.VocabSize)
                {
                    _agent = new CodeGenerationAgent(input.Tokenizer);
                }
            }
        }

        private static GraphSnapshot CreateDefaultGraphSnapshot(FunctionVectorSpace functionSpace)
        {
            if (functionSpace == null || functionSpace.Functions.Count == 0)
                return new GraphSnapshot
                {
                    SnapshotId = "default-empty",
                    Name = "DefaultEmptyGraph",
                    CreatedAt = DateTime.UtcNow,
                    Nodes = new List<GraphNode>(),
                    Edges = new List<GraphEdge>(),
                    NodeCount = 0,
                    EdgeCount = 0
                };

            var nodes = new List<GraphNode>();
            var edges = new List<GraphEdge>();
            int nodeIdx = 0, edgeIdx = 0;

            foreach (var func in functionSpace.Functions)
            {
                nodes.Add(new GraphNode
                {
                    Id = $"n{nodeIdx}",
                    Name = func.Name,
                    NodeType = "Function"
                });
                nodeIdx++;
            }

            foreach (var func in functionSpace.Functions)
            {
                foreach (var callee in func.Calls)
                {
                    edges.Add(new GraphEdge
                    {
                        Id = $"e{edgeIdx}",
                        SourceId = func.Name,
                        TargetId = callee,
                        EdgeType = "Call"
                    });
                    edgeIdx++;
                }
            }

            return new GraphSnapshot
            {
                SnapshotId = "default-from-functionspace",
                Name = "DefaultGraph",
                CreatedAt = DateTime.UtcNow,
                Nodes = nodes,
                Edges = edges,
                NodeCount = nodes.Count,
                EdgeCount = edges.Count
            };
        }
    }
}



