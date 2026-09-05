#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Pipeline
{
    public class FeaturePipeline : IFeaturePipeline
    {
        private readonly AgentFlowConfig _config;
        private readonly FeatureExtractor _extractor;

        public FeatureExtractor Extractor => _extractor;

        public PoolingStrategy Pooling
        {
            get => _extractor.Pooling;
            set => _extractor.Pooling = value;
        }

        public FeaturePipeline(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _extractor = new FeatureExtractor(config.ModelDim);

            _extractor.Initialize(
                config.NumLayers,
                config.NumHeads,
                config.FfnHiddenDim,
                config.Activation,
                config.Seed.HasValue ? new Random(config.Seed.Value) : null);
        }

        public Tensor Extract(Tensor X)
        {
            return _extractor.Extract(X);
        }

        public Task<Tensor> ExtractAsync(Tensor X,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Extract(X);
            }, cancellationToken);
        }
    }
}
