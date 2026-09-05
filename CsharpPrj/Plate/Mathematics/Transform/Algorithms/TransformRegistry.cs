#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class TransformRegistry : ITransformRegistry
    {
        private readonly ConcurrentDictionary<string, object> _transforms = new();
        private bool _disposed;

        public void Register<TInput, TOutput>(string name, ITransform<TInput, TOutput> transform)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Transform name cannot be null or empty");

            _transforms[name] = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public bool HasTransform(string name)
        {
            return _transforms.ContainsKey(name);
        }

        public IReadOnlyCollection<string> GetTransformNames()
        {
            return _transforms.Keys.ToList().AsReadOnly();
        }

        public async Task<TransformResult<TOutput>> TransformAsync<TInput, TOutput>(
            string transformName,
            TInput input,
            TransformConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_transforms.TryGetValue(transformName, out var transformObj))
                throw new KeyNotFoundException($"Transform '{transformName}' not registered");

            if (transformObj is ITransform<TInput, TOutput> transform)
            {
                return await transform.TransformAsync(
                    input,
                    config ?? TransformConfig.Default,
                    progress,
                    cancellationToken);
            }

            throw new InvalidOperationException(
                $"Transform '{transformName}' does not support input type {typeof(TInput).Name} " +
                $"or output type {typeof(TOutput).Name}");
        }

        public ITransform<TInput, TOutput> GetTransform<TInput, TOutput>(string name)
        {
            if (!_transforms.TryGetValue(name, out var transformObj))
                throw new KeyNotFoundException($"Transform '{name}' not registered");

            if (transformObj is ITransform<TInput, TOutput> transform)
                return transform;

            throw new InvalidCastException(
                $"Transform '{name}' has incorrect type signature");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _transforms.Clear();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
