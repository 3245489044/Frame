#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Transformers.Core;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.AgentFlow.Interfaces
{
    public interface IFeaturePipeline
    {
        PoolingStrategy Pooling { get; set; }

        Tensor Extract(Tensor X);

        Task<Tensor> ExtractAsync(Tensor X,
            CancellationToken cancellationToken = default);
    }
}
