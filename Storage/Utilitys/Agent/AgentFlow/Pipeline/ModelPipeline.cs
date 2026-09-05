#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.AgentFlow.Builders;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.AgentFlow.Models;
using Agent.Transformers.Core;
using Agent.Transformers.Models;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Pipeline
{
    public class ModelPipeline : IModelPipeline
    {
        private readonly AgentFlowConfig _config;
        private readonly TransformerBuilder _transformerBuilder;
        private readonly RuntimeBuilder _runtimeBuilder;

        private TransformerModel? _transformerModel;
        private RuntimeEngine? _runtimeEngine;

        public TransformerModel? TransformerModel => _transformerModel;

        public RuntimeEngine? RuntimeEngine => _runtimeEngine;

        public ModelPipeline(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transformerBuilder = new TransformerBuilder(config);
            _runtimeBuilder = new RuntimeBuilder(config);
        }

        private void EnsureTransformerModel()
        {
            _transformerModel ??= _transformerBuilder.Build();
        }

        private void EnsureRuntimeEngine()
        {
            _runtimeEngine ??= _runtimeBuilder.Build();
        }

        public Tensor Forward(Tensor input, IProgress<ComputationProgress>? progress = null)
        {
            if (_config.Mode == AgentMode.TextGeneration ||
                _config.Mode == AgentMode.FeatureExtraction)
            {
                EnsureTransformerModel();
                throw new InvalidOperationException(
                    "TextGeneration mode requires Generate() method. " +
                    "Use Forward(int[,] tokenIds) via TransformerModel directly.");
            }
            else
            {
                EnsureRuntimeEngine();
                return _runtimeEngine.Predict(input);
            }
        }

        public Task<Tensor> ForwardAsync(Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Forward(input, progress);
            }, cancellationToken);
        }

        public int[] Generate(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            EnsureTransformerModel();
            return _transformerModel.Generate(
                inputTokens, maxNewTokens, temperature, progress);
        }

        public int[] GenerateWithCache(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            EnsureTransformerModel();
            return _transformerModel.GenerateWithCache(
                inputTokens, maxNewTokens, temperature, progress);
        }

        public void Fit(Tensor X, Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            EnsureRuntimeEngine();
            _runtimeEngine.Fit(X, y, progressCallback: progressCallback);
        }

        public void Fit(double[,] X, double[,] y,
            Action<int, int, double>? progressCallback = null)
        {
            EnsureRuntimeEngine();
            _runtimeEngine.Fit(X, y, progressCallback: progressCallback);
        }

        public Tensor Predict(Tensor X)
        {
            EnsureRuntimeEngine();
            return _runtimeEngine.Predict(X);
        }

        public double[,] Predict(double[,] X)
        {
            EnsureRuntimeEngine();
            return _runtimeEngine.Predict(X);
        }

        public (int action, double logProb) SampleAction(Tensor logits, double temperature = 1.0)
        {
            if (_config.Mode != AgentMode.ReinforcementLearning)
                throw new InvalidOperationException("SampleAction is only available in ReinforcementLearning mode.");

            EnsureRuntimeEngine();
            var strategy = _runtimeEngine.Strategy;

            if (strategy is PolicyGradientFitting pgStrategy)
            {
                return pgStrategy.SampleAction(_runtimeEngine.Context, logits, temperature);
            }
            else if (strategy is PPOFitting ppoStrategy)
            {
                return ppoStrategy.SampleAction(_runtimeEngine.Context, logits, temperature);
            }

            throw new InvalidOperationException(
                $"Current strategy {strategy?.Name} does not support action sampling.");
        }

        public void RecordStep(Tensor state, int action, double logProb, double reward, bool done, double value)
        {
            if (_config.Mode != AgentMode.ReinforcementLearning)
                throw new InvalidOperationException("RecordStep is only available in ReinforcementLearning mode.");

            EnsureRuntimeEngine();
            var strategy = _runtimeEngine.Strategy;
            var context = _runtimeEngine.Context;

            if (strategy is PolicyGradientFitting pgStrategy)
            {
                pgStrategy.RecordStep(context, state, action, logProb, reward, done, value);
            }
            else if (strategy is PPOFitting ppoStrategy)
            {
                ppoStrategy.RecordStep(context, state, action, logProb, reward, done, value);
            }
        }

        public void UpdatePolicy()
        {
            if (_config.Mode != AgentMode.ReinforcementLearning)
                throw new InvalidOperationException("UpdatePolicy is only available in ReinforcementLearning mode.");

            EnsureRuntimeEngine();
            var strategy = _runtimeEngine.Strategy;
            var context = _runtimeEngine.Context;

            if (strategy is PolicyGradientFitting pgStrategy)
            {
                pgStrategy.UpdateFromTrajectory(context);
            }
            else if (strategy is PPOFitting ppoStrategy)
            {
                ppoStrategy.UpdateFromTrajectory(context);
            }
        }

        public double PredictValue(Tensor features)
        {
            if (_config.Mode != AgentMode.ReinforcementLearning)
                throw new InvalidOperationException("PredictValue is only available in ReinforcementLearning mode.");

            EnsureRuntimeEngine();
            var strategy = _runtimeEngine.Strategy;
            var context = _runtimeEngine.Context;

            if (strategy is PPOFitting ppoStrategy)
            {
                return ppoStrategy.PredictValue(context, features);
            }

            if (context.ValueWeight != null && context.ValueBias != null)
            {
                double value = 0;
                for (int r = 0; r < context.Config.ModelDim; r++)
                    value += features[0, 0, r] * context.ValueWeight[0, r, 0];
                value += context.ValueBias[0, 0, 0];
                return value;
            }

            return 0;
        }

        public Tensor GetLogits(Tensor hidden)
        {
            EnsureTransformerModel();
            return _transformerModel.GetLogits(hidden);
        }

        public Tensor Forward(int[,] tokenIds,
            IProgress<ComputationProgress>? progress = null)
        {
            EnsureTransformerModel();
            return _transformerModel.Forward(tokenIds, progress);
        }

        public Task<Tensor> ForwardAsync(int[,] tokenIds,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureTransformerModel();
            return _transformerModel.ForwardAsync(tokenIds, progress, cancellationToken);
        }
    }
}
