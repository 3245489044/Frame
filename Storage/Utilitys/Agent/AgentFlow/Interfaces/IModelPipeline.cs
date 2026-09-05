#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Interfaces
{
    public interface IModelPipeline
    {
        Tensor Forward(Tensor input, IProgress<ComputationProgress>? progress = null);

        Task<Tensor> ForwardAsync(Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default);

        int[] Generate(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null);

        int[] GenerateWithCache(int[] inputTokens, int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null);

        void Fit(Tensor X, Tensor y,
            Action<int, int, double>? progressCallback = null);

        Tensor Predict(Tensor X);

        Tensor GetLogits(Tensor hidden);
    }
}
