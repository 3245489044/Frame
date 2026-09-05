#region USINGS
using System;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Models;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Builders
{
    public class RuntimeBuilder
    {
        private AgentFlowConfig _config;
        private RuntimeEngine? _engine;

        public bool IsBuilt => _engine != null;

        public RuntimeBuilder(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public RuntimeEngine Build()
        {
            if (_engine == null)
            {
                var runtimeConfig = _config.ToRuntimeConfig();

                IFittingStrategy? strategy = _config.Mode switch
                {
                    AgentMode.Classification => new GradientFreeFitting(),
                    AgentMode.TimeSeries => new OnlineBackpropFitting(),
                    AgentMode.Regression => null,
                    AgentMode.ReinforcementLearning => _config.FittingStrategy switch
                    {
                        FittingType.PolicyGradient => new PolicyGradientFitting(),
                        FittingType.PPO => new PPOFitting(),
                        _ => new PolicyGradientFitting()
                    },
                    _ => null
                };

                _engine = new RuntimeEngine(runtimeConfig, strategy);
            }
            return _engine;
        }

        public RuntimeEngine Rebuild(AgentFlowConfig? newConfig = null)
        {
            if (newConfig != null)
            {
                _config = newConfig;
            }
            _engine = null;
            return Build();
        }

        public void Reset()
        {
            _engine = null;
        }
    }
}
