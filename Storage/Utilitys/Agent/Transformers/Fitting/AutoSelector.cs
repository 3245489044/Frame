#region USINGS
using System;
using System.Collections.Generic;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public static class AutoSelector
    {
        private static readonly Dictionary<FittingType, Func<IFittingStrategy>> _registry = new()
        {
            { FittingType.LeastSquares, () => new LeastSquaresFitting() },
            { FittingType.GradientFree, () => new GradientFreeFitting() },
            { FittingType.Online, () => new OnlineFitting() },
            { FittingType.LayerwiseBP, () => new LayerwiseFitting() },
            { FittingType.OnlineBackprop, () => new OnlineBackpropFitting() },
            { FittingType.PolicyGradient, () => new PolicyGradientFitting() },
            { FittingType.PPO, () => new PPOFitting() }
        };

        public static void Register(FittingType type, Func<IFittingStrategy> factory)
        {
            _registry[type] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static IFittingStrategy Create(FittingType type)
        {
            if (_registry.TryGetValue(type, out var factory))
            {
                return factory();
            }
            return new LeastSquaresFitting();
        }

        public static IFittingStrategy Select(
            RuntimeConfig config,
            int numSamples,
            int inputDim,
            int outputDim,
            bool isStreaming = false)
        {
            if (config.FittingStrategy != FittingType.LeastSquares)
            {
                return Create(config.FittingStrategy);
            }

            if (isStreaming)
            {
                return config.NumLayers > 1
                    ? Create(FittingType.OnlineBackprop)
                    : Create(FittingType.Online);
            }

            switch (config.OutputType)
            {
                case OutputType.Classification:
                    return numSamples < 500
                        ? Create(FittingType.GradientFree)
                        : Create(FittingType.LayerwiseBP);

                case OutputType.Sequence:
                    return Create(FittingType.OnlineBackprop);

                case OutputType.Regression:
                default:
                    if (numSamples < 100)
                    {
                        return Create(FittingType.GradientFree);
                    }
                    else if (numSamples < 1000)
                    {
                        return Create(FittingType.LayerwiseBP);
                    }
                    else
                    {
                        return Create(FittingType.LeastSquares);
                    }
            }
        }

        public static string GetAvailableStrategiesSummary()
        {
            var summaries = new List<string>();

            foreach (var kvp in _registry)
            {
                var strategy = kvp.Value();
                summaries.Add($"  [{strategy.FittingType}] {strategy.Name}");
            }

            return $"Available Strategies ({_registry.Count}):\n" +
                   string.Join("\n", summaries);
        }

        public static string GetRecommendation(RuntimeConfig config, int numSamples)
        {
            var recommended = Select(config, numSamples, config.ModelDim, 1);
            var leastSquares = Create(FittingType.LeastSquares);
            var layerwiseBP = Create(FittingType.LayerwiseBP);
            var onlineBP = Create(FittingType.OnlineBackprop);

            return $"""
                AutoSelector Recommendation:
                  Samples: {numSamples}
                  Task: {config.OutputType}
                  Layers: {config.NumLayers}
                
                Recommended Strategy: [{recommended.FittingType}] {recommended.Name}
                
                Alternative Options:
                  - [{leastSquares.FittingType}] {leastSquares.Name}: Fastest for linear problems
                  - [{layerwiseBP.FittingType}] {layerwiseBP.Name}: Simulates backpropagation
                  - [{onlineBP.FittingType}] {onlineBP.Name}: Best for streaming data
                """;
        }
    }
}
