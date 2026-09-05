#region USINGS
using System;
using Agent.AgentFlow.Config;
using Agent.Transformers.Models;
#endregion

namespace Agent.AgentFlow.Builders
{
    public class TransformerBuilder
    {
        private AgentFlowConfig _config;
        private TransformerModel? _model;

        public bool IsBuilt => _model != null;

        public TransformerBuilder(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public TransformerModel Build()
        {
            if (_model == null)
            {
                var transformerConfig = _config.ToTransformerConfig();
                _model = new TransformerModel(transformerConfig);
            }
            return _model;
        }

        public TransformerModel Rebuild(AgentFlowConfig? newConfig = null)
        {
            if (newConfig != null)
            {
                _config = newConfig;
            }
            _model = null;
            return Build();
        }

        public void Reset()
        {
            _model = null;
        }
    }
}
