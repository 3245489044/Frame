#region USINGS
using System;
using System.Collections.Generic;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Pipeline
{
    public class DataPipeline : IDataPipeline
    {
        private readonly AgentFlowConfig _config;
        private readonly Normalizer _normalizer;
        private readonly DataEncoder _encoder;

        public Normalizer Normalizer => _normalizer;

        public DataEncoder Encoder => _encoder;

        public DataPipeline(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _normalizer = new Normalizer(NormalizationType.ZScore);
            _encoder = new DataEncoder();
        }

        public void Fit(Tensor X)
        {
            _normalizer.Fit(X);
        }

        public Tensor Normalize(Tensor X)
        {
            if (!_normalizer.IsFitted)
            {
                _normalizer.Fit(X);
            }
            return _normalizer.Transform(X);
        }

        public Tensor Denormalize(Tensor X)
        {
            return _normalizer.InverseTransform(X);
        }

        public Tensor EncodeOneHot(string[] labels)
        {
            if (!_encoder.IsFitted)
            {
                _encoder.Fit(labels);
            }
            return _encoder.EncodeOneHot(labels);
        }

        public int[] EncodeLabel(string[] labels)
        {
            if (!_encoder.IsFitted)
            {
                _encoder.Fit(labels);
            }
            return _encoder.EncodeLabel(labels);
        }

        public string[] Decode(int[] indices)
        {
            return _encoder.Decode(indices);
        }
    }
}
