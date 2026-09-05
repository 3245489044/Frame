#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public interface ITransform<TInput, TOutput>
    {
        Task<TransformResult<TOutput>> TransformAsync(
            TInput input,
            TransformConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);
    }

    public interface ITransformRegistry : IDisposable
    {
        Task<TransformResult<TOutput>> TransformAsync<TInput, TOutput>(
            string transformName,
            TInput input,
            TransformConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);

        void Register<TInput, TOutput>(string name, ITransform<TInput, TOutput> transform);

        bool HasTransform(string name);

        System.Collections.Generic.IReadOnlyCollection<string> GetTransformNames();
    }
}
