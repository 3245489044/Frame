
#region USINGS
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.VectorSpace;
using SkeletonFlow;
using WorkFlowMethod.GraphFramework.Snapshots;
#endregion

namespace CodeRLFramework.Pipeline.Skeletons
{
    public class LLMNavSelectionResult
    {
        public bool IsSuccess { get; set; }

        public string? SelectedNode { get; set; }

        public double Confidence { get; set; }

        public bool IsFallback { get; set; }

        public List<string> Candidates { get; set; } = new();

        public string? ErrorMessage { get; set; }
    }

    public class LLMNavSelectSkeleton : SkeletonBase<CodeRLContext, CodeRLContext>
    {
        private readonly CodeGenerationAgent _agent;
        private readonly Random _rng;

        public LLMNavSelectSkeleton(CodeGenerationAgent agent)
            : base("LLMNavSelectSkeleton", "大模型作为引用图导航引擎，选择下一函数调用节点")
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _rng = new Random();
        }

        protected override CodeRLContext ExecuteCore(CodeRLContext input, SkeletonContext context)
        {
            return ExecuteCoreAsync(input, context).GetAwaiter().GetResult();
        }

        protected override Task<CodeRLContext> ExecuteCoreAsync(CodeRLContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.GraphSnapshot == null)
                throw new InvalidOperationException("GraphSnapshot 未设置，请先执行 CodeExtractSkeleton");
            if (input.FunctionSpace == null)
                throw new InvalidOperationException("FunctionSpace 未设置");

            var navContext = GetOrCreateNavContext(input);

            var candidates = GetCandidates(input.GraphSnapshot, navContext.CurrentNode!);

            var unvisitedCandidates = navContext.GetUnvisitedCandidates(candidates);
            if (!navContext.ShouldContinue(unvisitedCandidates))
            {
                context.Items["LLMNavSelectSkeleton.Info"] =
                    $"导航结束: {navContext.GetPathSummary()}";
                return Task.FromResult(input);
            }

            var selection = PerformLLMSelection(input, navContext, unvisitedCandidates, context);

            var step = navContext.RecordStep(
                navContext.CurrentNode!,
                selection.SelectedNode!,
                candidates,
                selection.Confidence,
                selection.IsFallback);

            input.Properties["GraphNavigationContext"] = navContext;

            AddSelectedNodeToTokens(input, selection.SelectedNode!);

            context.Items["LLMNavSelectSkeleton.Info"] =
                $"导航选择: 当前={step.CurrentNode} → 选择={step.SelectedNode}, " +
                $"置信度={step.Confidence:F2}, " +
                (step.IsFallback ? "[RL回退]" : "[LLM直接]") + ", " +
                $"已访问={navContext.VisitedPath.Count}/{navContext.MaxSteps}";

            return Task.FromResult(input);
        }

        private static GraphNavigationContext GetOrCreateNavContext(CodeRLContext input)
        {
            if (input.Properties.TryGetValue("GraphNavigationContext", out var existing) &&
                existing is GraphNavigationContext existingNav)
            {
                return existingNav;
            }

            var navContext = new GraphNavigationContext
            {
                GraphSnapshotId = input.GraphSnapshot?.SnapshotId,
                StartNode = GetStartNode(input),
                CurrentNode = GetStartNode(input),
                MaxSteps = input.MaxStepsPerEpisode,
                ConfidenceThreshold = input.Weights?.ConfidenceThreshold ?? 0.3
            };

            if (navContext.StartNode != null)
            {
                navContext.VisitedSet.Add(navContext.StartNode);
                navContext.VisitedPath.Add(navContext.StartNode);
            }

            input.Properties["GraphNavigationContext"] = navContext;
            return navContext;
        }

        private static string? GetStartNode(CodeRLContext input)
        {
            if (!string.IsNullOrEmpty(input.TemplateCode))
            {
                var names = ExtractFunctionNames(input.TemplateCode);
                if (names.Count > 0)
                    return names[0];
            }

            if (input.FunctionSpace != null && input.FunctionSpace.Functions.Count > 0)
                return input.FunctionSpace.Functions[0].Name;

            return null;
        }

        private static List<string> GetCandidates(GraphSnapshot snapshot, string currentNode)
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

        private LLMNavSelectionResult PerformLLMSelection(
            CodeRLContext input,
            GraphNavigationContext navContext,
            List<string> unvisitedCandidates,
            SkeletonContext context)
        {
            var result = new LLMNavSelectionResult
            {
                Candidates = unvisitedCandidates
            };

            if (unvisitedCandidates.Count == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "无候选节点";
                return result;
            }

            if (unvisitedCandidates.Count == 1)
            {
                result.IsSuccess = true;
                result.SelectedNode = unvisitedCandidates[0];
                result.Confidence = 1.0;
                result.IsFallback = false;
                return result;
            }

            var prompt = BuildNavigationPrompt(navContext, unvisitedCandidates);

            var (selectedNode, confidence) = SelectWithLLM(prompt, unvisitedCandidates, context);

            if (confidence >= navContext.ConfidenceThreshold)
            {
                result.IsSuccess = true;
                result.SelectedNode = selectedNode;
                result.Confidence = confidence;
                result.IsFallback = false;
            }
            else
            {
                result.IsSuccess = true;
                result.SelectedNode = SelectWithRLFallback(input, unvisitedCandidates);
                result.Confidence = confidence;
                result.IsFallback = true;
            }

            return result;
        }

        private static string BuildNavigationPrompt(
            GraphNavigationContext navContext,
            List<string> candidates)
        {
            var visitedPath = navContext.VisitedPath.Count > 0
                ? string.Join(" → ", navContext.VisitedPath)
                : "(起始)";

            var candidateList = string.Join(", ", candidates);

            return $"当前在函数 [{navContext.CurrentNode}]，" +
                   $"已访问路径: {visitedPath}，" +
                   $"可选择的下一函数: [{candidateList}]。" +
                   $"请从候选函数中选择最合适的下一个调用。只需返回函数名。";
        }

        private (string selectedNode, double confidence) SelectWithLLM(
            string prompt,
            List<string> candidates,
            SkeletonContext context)
        {
            try
            {
                var promptTokens = EncodePrompt(prompt);

                var generatedTokens = _agent.Generate(promptTokens, maxSteps: 5, temperature: 0.5);

                var decodedNames = _agent.Tokenizer.DecodeToNames(generatedTokens);

                foreach (var name in decodedNames)
                {
                    var match = candidates.Find(c =>
                        c.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        double confidence = EstimateConfidence(generatedTokens, match);
                        return (match, confidence);
                    }
                }

                return (candidates[0], 0.1);
            }
            catch (Exception ex)
            {
                context.Items["LLMNavSelectSkeleton.LLMError"] = ex.Message;
                return (candidates[0], 0.0);
            }
        }

        private int[] EncodePrompt(string prompt)
        {
            var functionNames = ExtractFunctionNames(prompt);
            return _agent.Tokenizer.Encode(functionNames);
        }

        private static double EstimateConfidence(int[] generatedTokens, string matchedNode)
        {
            double baseConfidence = 0.7;
            
            int matchIndex = -1;
            for (int i = 0; i < generatedTokens.Length; i++)
            {
                if (generatedTokens[i] >= 4)
                {
                    matchIndex = i;
                    break;
                }
            }
            
            double positionBonus = matchIndex >= 0 
                ? 0.3 * (1.0 - (double)matchIndex / Math.Max(generatedTokens.Length, 1))
                : 0.0;
                
            return Math.Min(baseConfidence + positionBonus, 1.0);
        }

        private static string SelectWithRLFallback(
            CodeRLContext input,
            List<string> candidates)
        {
            var rng = new Random();
            return candidates[rng.Next(candidates.Count)];
        }

        private static void AddSelectedNodeToTokens(CodeRLContext input, string selectedNode)
        {
            if (input.Tokenizer == null) return;

            var nodeTokens = input.Tokenizer.Encode(new List<string> { selectedNode });
            var currentTokens = input.GeneratedTokens ?? Array.Empty<int>();
            input.GeneratedTokens = currentTokens.Concat(nodeTokens).ToArray();

            input.DecodedFunctionNames ??= new List<string>();
            input.DecodedFunctionNames.Add(selectedNode);
        }

        private static List<string> ExtractFunctionNames(string text)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(text)) return names;

            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]) ||
                    text[i] == ';' || text[i] == '{' ||
                    text[i] == '}' || text[i] == '(' ||
                    text[i] == ')' || text[i] == ',' ||
                    text[i] == '[' || text[i] == ']')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(text[i]) || text[i] == '_')
                {
                    int start = i;
                    while (i < text.Length &&
                           (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        i++;

                    string word = text[start..i];
                    if (!IsCSharpKeyword(word) && word.Length > 1)
                    {
                        names.Add(word);
                    }
                }
                else
                {
                    i++;
                }
            }

            return names;
        }

        private static bool IsCSharpKeyword(string word)
        {
            return word switch
            {
                "using" or "namespace" or "class" or "struct" or "interface" or
                "public" or "private" or "protected" or "internal" or "static" or
                "void" or "int" or "string" or "bool" or "double" or "float" or
                "var" or "new" or "return" or "if" or "else" or "for" or "foreach" or
                "while" or "do" or "switch" or "case" or "break" or "continue" or
                "try" or "catch" or "finally" or "throw" or "async" or "await" or
                "this" or "base" or "null" or "true" or "false" or "in" or "out" or
                "ref" or "readonly" or "const" or "override" or "virtual" or
                "abstract" or "sealed" or "partial" or "record" or "init" or
                "get" or "set" or "value" or "where" or "select" or "from" => true,
                _ => false
            };
        }
    }
}
