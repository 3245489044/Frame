#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
using Agent.AgentFlow.Interfaces;
#endregion

namespace Agent.AgentFlow.Interfaces
{
    public interface IAgentPipeline
    {
        IDataPipeline Data { get; }

        IFeaturePipeline Features { get; }

        IModelPipeline Model { get; }

        IOutputPipeline Output { get; }

        Tensor Execute(Tensor input, IProgress<ComputationProgress>? progress = null);

        Task<Tensor> ExecuteAsync(Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
