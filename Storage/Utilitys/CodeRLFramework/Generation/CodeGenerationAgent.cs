#region extern alias
extern alias AgentAlias;
#endregion

using AgentAlias::Agent.AgentFlow;
using AgentAlias::Agent.AgentFlow.Config;
using AgentAlias::Agent.AgentFlow.Models;
using CodeRLFramework.Input;
using CodeRLFramework.Pipeline;

namespace CodeRLFramework.Generation
{
    public class CodeGenerationAgent
    {
        private readonly CodeTokenizer _tokenizer;
        private readonly AgentFlow _agentFlow;
        private readonly AgentFlowConfig _config;
        private readonly Random _rng;

        public List<int> CurrentSequence { get; private set; }

        public double[]? CurrentLogits { get; private set; }

        public AgentFlow InnerAgentFlow => _agentFlow;

        public CodeTokenizer Tokenizer => _tokenizer;

        public CodeGenerationAgent(
            CodeTokenizer tokenizer,
            AgentFlowConfig? config = null)
        {
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            _rng = new Random();

            _config = (config ?? AgentFlowConfig.Default) with
            {
                VocabSize = tokenizer.VocabSize,
                Mode = AgentMode.TextGeneration,
                EosTokenId = CodeTokenizer.EOS_TOKEN
            };

            Console.Error.WriteLine(
                $"[CodeGenerationAgent] tokenizer.VocabSize={tokenizer.VocabSize}, " +
                $"_config.VocabSize={_config.VocabSize}");

            _agentFlow = AgentFlow.ForTextGeneration(_config);

            Console.Error.WriteLine(
                $"[CodeGenerationAgent] _agentFlow.Config.VocabSize={_agentFlow.Config.VocabSize}");

            CurrentSequence = new List<int>();
        }

        public int[] Generate(
            int[] contextTokens,
            int maxSteps = 50,
            double temperature = 1.0)
        {
            var result = _agentFlow.GenerateWithCache(
                contextTokens,
                maxNewTokens: maxSteps,
                temperature: temperature);

            CurrentSequence = result.ToList();
            return result;
        }

        public int SelectAction(
            double[] logits,
            double temperature = 1.0,
            double epsilon = 0.1)
        {
            if (_rng.NextDouble() < epsilon)
            {
                return _rng.Next(CodeTokenizer.SPECIAL_TOKENS, _tokenizer.VocabSize);
            }

            return SampleFromLogits(logits, temperature);
        }

        public double[] GetActionProbs(double[] logits, double temperature = 1.0)
        {
            var scaled = logits.Select(l => l / temperature).ToArray();
            return Softmax(scaled);
        }

        public double GetLogProb(int action, double[] logits)
        {
            var probs = Softmax(logits);
            double prob = action >= 0 && action < probs.Length ? probs[action] : 0;
            return Math.Log(Math.Max(prob, 1e-10));
        }


        private int SampleFromLogits(double[] logits, double temperature)
        {
            var probs = GetActionProbs(logits, temperature);

            double sample = _rng.NextDouble();
            double cumulative = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                cumulative += probs[i];
                if (sample <= cumulative)
                    return i;
            }

            return probs.Length - 1;
        }

        private static double[] Softmax(double[] logits)
        {
            double max = logits.Max();
            var exp = logits.Select(l => Math.Exp(l - max)).ToArray();
            double sum = exp.Sum();
            return sum > 0 ? exp.Select(e => e / sum).ToArray() : new double[logits.Length];
        }
    }
}
