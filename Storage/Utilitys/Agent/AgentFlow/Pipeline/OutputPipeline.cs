#region USINGS
using System;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.AgentFlow.Models;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Pipeline
{
    public class OutputPipeline : IOutputPipeline
    {
        private readonly AgentFlowConfig _config;
        private readonly DataPipeline _dataPipeline;
        private readonly ClassificationHead? _classificationHead;

        public OutputPipeline(AgentFlowConfig config, DataPipeline dataPipeline)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dataPipeline = dataPipeline ?? throw new ArgumentNullException(nameof(dataPipeline));

            if (config.Mode == AgentMode.Classification)
            {
                _classificationHead = new ClassificationHead(config.ModelDim, config.NumClasses);
            }
        }

        public Tensor Denormalize(Tensor output)
        {
            return _dataPipeline.Denormalize(output);
        }

        public Tensor GetProbabilities(Tensor logits)
        {
            return logits.Softmax(dim: 2);
        }

        public int[] GetPredictions(Tensor logits)
        {
            if (_classificationHead != null)
            {
                return _classificationHead.PredictClass(logits);
            }

            int batchSize = logits.BatchSize;
            var predictions = new int[batchSize];
            for (int b = 0; b < batchSize; b++)
            {
                double maxVal = double.MinValue;
                int maxIdx = 0;
                for (int d = 0; d < logits.Dimension; d++)
                {
                    if (logits[b, 0, d] > maxVal)
                    {
                        maxVal = logits[b, 0, d];
                        maxIdx = d;
                    }
                }
                predictions[b] = maxIdx;
            }
            return predictions;
        }

        public int SampleToken(Tensor logits, double temperature = 1.0)
        {
            var rng = new Random();

            if (temperature <= 0)
            {
                return (int)logits.ArgMax(dim: 2)[0, 0, 0];
            }
            else
            {
                var probs = logits.Divide(temperature).Softmax(dim: 2);
                double sample = rng.NextDouble();
                double cumulative = 0;
                for (int d = 0; d < probs.Dimension; d++)
                {
                    cumulative += probs[0, 0, d];
                    if (sample < cumulative)
                        return d;
                }
                return probs.Dimension - 1;
            }
        }

        public string GetSummary()
        {
            return $"AgentFlow: Mode={_config.Mode}, " +
                   $"ModelDim={_config.ModelDim}, " +
                   $"NumLayers={_config.NumLayers}, " +
                   $"NumHeads={_config.NumHeads}";
        }
    }
}
