using CodeRLFramework.Generation;
using CodeRLFramework.Input;
using CodeRLFramework.Output;
using CodeRLFramework.Pipeline;
using CodeRLFramework.VectorSpace;

namespace CodeRLFramework.RL
{
    [Obsolete("请使用 CodeRLPipeline 替代 RLTrainer。CodeRLPipeline 提供 Skeleton 化管线，支持运行时动态修改和配置驱动。")]
    public class RLTrainer
    {
        private readonly CodeGenerationAgent _agent;
        private readonly CodeTokenizer _tokenizer;
        private readonly VectorToCodeConverter _converter;
        private readonly RewardFunction _rewardFunction;
        private readonly PolicyGradient _policyGradient;
        private readonly FunctionVectorSpace _functionSpace;

        public int Episode { get; private set; }

        public List<double> RewardHistory { get; }

        public string BestCode { get; private set; } = string.Empty;

        public double BestReward { get; private set; } = double.MinValue;

        public bool IsTraining { get; private set; }

        public int MaxStepsPerEpisode { get; set; } = 50;

        public double ConvergenceThreshold { get; set; } = 0.01;

        public int ConvergenceWindow { get; set; } = 10;

        public RLTrainer(
            CodeGenerationAgent agent,
            CodeTokenizer tokenizer,
            VectorToCodeConverter converter,
            RewardFunction rewardFunction,
            PolicyGradient policyGradient,
            FunctionVectorSpace functionSpace)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _rewardFunction = rewardFunction ?? throw new ArgumentNullException(nameof(rewardFunction));
            _policyGradient = policyGradient ?? throw new ArgumentNullException(nameof(policyGradient));
            _functionSpace = functionSpace ?? throw new ArgumentNullException(nameof(functionSpace));

            RewardHistory = new List<double>();
            Episode = 0;
        }

        public double TrainEpisode(string templateCode, string testCode = "")
        {
            IsTraining = true;
            Episode++;

            try
            {
                var functionNames = ExtractFunctionNames(templateCode);

                var contextTokens = _tokenizer.Encode(functionNames);

                var generatedTokens = _agent.Generate(contextTokens, MaxStepsPerEpisode);

                var decodedFunctionNames = _tokenizer.DecodeToNames(generatedTokens);

                var outputSpace = BuildOutputSpace(decodedFunctionNames);

                var generatedCode = _converter.Convert(outputSpace, templateCode);

                double reward = _rewardFunction.Calculate(generatedCode, testCode);

                RecordTrajectory(decodedFunctionNames, reward);

                _policyGradient.EndTrajectory();

                RewardHistory.Add(reward);

                if (reward > BestReward)
                {
                    BestReward = reward;
                    BestCode = generatedCode;
                }

                return reward;
            }
            finally
            {
                IsTraining = false;
            }
        }

        public TrainingResult Train(
            string templateCode,
            string testCode = "",
            int maxEpisodes = 100,
            Action<TrainingProgress>? onEpisodeComplete = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int episode = 0; episode < maxEpisodes; episode++)
            {
                double reward = TrainEpisode(templateCode, testCode);

                onEpisodeComplete?.Invoke(new TrainingProgress
                {
                    Episode = Episode,
                    Reward = reward,
                    BestReward = BestReward,
                    AverageReward = _policyGradient.AverageReward,
                    IsConverged = false
                });

                if (IsConverged())
                {
                    onEpisodeComplete?.Invoke(new TrainingProgress
                    {
                        Episode = Episode,
                        Reward = reward,
                        BestReward = BestReward,
                        AverageReward = _policyGradient.AverageReward,
                        IsConverged = true
                    });
                    break;
                }
            }

            stopwatch.Stop();

            return new TrainingResult
            {
                TotalEpisodes = Episode,
                BestReward = BestReward,
                BestCode = BestCode,
                AverageReward = _policyGradient.AverageReward,
                TrainingTime = stopwatch.Elapsed,
                RewardHistory = RewardHistory.ToArray(),
                IsConverged = IsConverged()
            };
        }

        private static List<string> ExtractFunctionNames(string templateCode)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(templateCode))
                return names;

            int i = 0;
            while (i < templateCode.Length)
            {
                if (char.IsWhiteSpace(templateCode[i]) || 
                    templateCode[i] == ';' || 
                    templateCode[i] == '{' || 
                    templateCode[i] == '}' ||
                    templateCode[i] == '(' ||
                    templateCode[i] == ')')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(templateCode[i]) || templateCode[i] == '_')
                {
                    int start = i;
                    while (i < templateCode.Length && 
                           (char.IsLetterOrDigit(templateCode[i]) || templateCode[i] == '_'))
                        i++;

                    string word = templateCode[start..i];

                    if (!IsCSharpKeyword(word))
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

        private OutputVectorSpace BuildOutputSpace(List<string> functionNames)
        {
            var vectors = new List<OutputVector>();

            foreach (var name in functionNames)
            {
                var matches = _functionSpace.SearchByName(name);
                if (matches.Count > 0)
                {
                    var func = matches[0];
                    vectors.Add(new OutputVector
                    {
                        FunctionId = func.Id,
                        FunctionName = func.Name,
                        Vector = func.GetFullVector(),
                        Probability = 1.0
                    });
                }
            }

            return new OutputVectorSpace
            {
                Vectors = vectors,
                Confidence = vectors.Count > 0
                    ? vectors.Average(v => v.Probability)
                    : 0,
                Temperature = 1.0
            };
        }

        private void RecordTrajectory(List<string> functionNames, double finalReward)
        {
            for (int i = 0; i < functionNames.Count; i++)
            {
                int actionId = FindFunctionIndex(functionNames[i]);

                double[] state = new double[]
                {
                    i / (double)Math.Max(MaxStepsPerEpisode, 1),
                    functionNames.Count / (double)Math.Max(MaxStepsPerEpisode, 1),
                    finalReward > 0 ? 1.0 : 0.0
                };

                double stepReward = functionNames.Count > 0 ? finalReward / functionNames.Count : 0;

                _policyGradient.RecordStep(actionId, stepReward, state);
            }
        }

        private int FindFunctionIndex(string functionName)
        {
            for (int i = 0; i < _functionSpace.Functions.Count; i++)
            {
                if (_functionSpace.Functions[i].Name.Equals(functionName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        private bool IsConverged()
        {
            if (RewardHistory.Count < ConvergenceWindow)
                return false;

            var recent = RewardHistory.TakeLast(ConvergenceWindow).ToArray();
            double maxDeviation = recent.Max() - recent.Min();

            return maxDeviation < ConvergenceThreshold && recent.Average() > 0.5;
        }

        public void Reset()
        {
            Episode = 0;
            RewardHistory.Clear();
            BestCode = string.Empty;
            BestReward = double.MinValue;
            _policyGradient.Reset();
        }
    }

    public class TrainingProgress
    {
        public int Episode { get; set; }

        public double Reward { get; set; }

        public double BestReward { get; set; }

        public double AverageReward { get; set; }

        public bool IsConverged { get; set; }
    }

    public class TrainingResult
    {
        public int TotalEpisodes { get; set; }

        public double BestReward { get; set; }

        public string BestCode { get; set; } = string.Empty;

        public double AverageReward { get; set; }

        public TimeSpan TrainingTime { get; set; }

        public double[] RewardHistory { get; set; } = Array.Empty<double>();

        public bool IsConverged { get; set; }
    }
}
