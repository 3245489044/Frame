#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.AgentFlow.Builders;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.AgentFlow.Models;
using Agent.AgentFlow.Pipeline;
using Agent.Transformers.Core;
using Agent.Transformers.Models;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow
{
    public class AgentFlow
    {
        #region 私有字段

        private readonly AgentFlowConfig _config;
        private readonly IAgentPipeline _pipeline;

        #endregion

        #region 构造函数

        public AgentFlow(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _pipeline = new AgentPipeline(config);
        }

        #endregion

        #region 公共属性

        public AgentFlowConfig Config => _config;

        public IAgentPipeline Pipeline => _pipeline;

        public IDataPipeline Data => _pipeline.Data;

        public IFeaturePipeline Features => _pipeline.Features;

        public IModelPipeline Model => _pipeline.Model;

        public IOutputPipeline Output => _pipeline.Output;

        #endregion

        #region 快捷创建方法

        public static AgentFlow ForTextGeneration(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with { Mode = AgentMode.TextGeneration });
        }

        public static AgentFlow ForRegression(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with { Mode = AgentMode.Regression });
        }

        public static AgentFlow ForClassification(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with { Mode = AgentMode.Classification });
        }

        public static AgentFlow ForFeatureExtraction(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with { Mode = AgentMode.FeatureExtraction });
        }

        public static AgentFlow ForTimeSeries(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with { Mode = AgentMode.TimeSeries });
        }

        public static AgentFlow ForReinforcementLearning(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with
            {
                Mode = AgentMode.ReinforcementLearning,
                FittingStrategy = FittingType.PolicyGradient
            });
        }

        public static AgentFlow ForPPO(AgentFlowConfig? config = null)
        {
            var cfg = config ?? AgentFlowConfig.Default;
            return new AgentFlow(cfg with
            {
                Mode = AgentMode.ReinforcementLearning,
                FittingStrategy = FittingType.PPO
            });
        }

        public static AgentFlow FromTransformerConfig(TransformerConfig config)
        {
            return new AgentFlow(AgentFlowConfig.FromTransformerConfig(config));
        }

        public static AgentFlow FromRuntimeConfig(RuntimeConfig config)
        {
            return new AgentFlow(AgentFlowConfig.FromRuntimeConfig(config));
        }

        #endregion

        #region 便捷方法

        public int[] Generate(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            return _pipeline.Model.Generate(inputTokens, maxNewTokens, temperature, progress);
        }

        public int[] GenerateWithCache(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            return _pipeline.Model.GenerateWithCache(inputTokens, maxNewTokens, temperature, progress);
        }

        public void Fit(Tensor X, Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            _pipeline.Model.Fit(X, y, progressCallback);
        }

        public Tensor Predict(Tensor X)
        {
            return _pipeline.Model.Predict(X);
        }

        public Tensor Execute(Tensor input,
            IProgress<ComputationProgress>? progress = null)
        {
            return _pipeline.Execute(input, progress);
        }

        public Task<Tensor> ExecuteAsync(Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return _pipeline.ExecuteAsync(input, progress, cancellationToken);
        }

        public string GetSummary()
        {
            return _pipeline.Output.GetSummary();
        }

        #endregion
    }
}
