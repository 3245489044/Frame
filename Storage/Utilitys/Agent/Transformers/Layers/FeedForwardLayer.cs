#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Layers
{
    public class FeedForwardLayer
    {
        private readonly int _modelDim;
        private readonly int _hiddenDim;
        private readonly Core.ActivationType _activation;

        private readonly Tensor _w1, _w2;
        private readonly Tensor _b1, _b2;
        
        internal int ModelDim => _modelDim;
        internal int HiddenDim => _hiddenDim;
        internal Tensor W1 => _w1;
        internal Tensor W2 => _w2;
        internal Tensor B1 => _b1;
        internal Tensor B2 => _b2;

        public FeedForwardLayer(
            int modelDim,
            int hiddenDim,
            Core.ActivationType activation = Core.ActivationType.GELU,
            Random? rng = null)
        {
            if (modelDim <= 0)
                throw new ArgumentException("modelDim must be positive", nameof(modelDim));
            if (hiddenDim <= 0)
                throw new ArgumentException("hiddenDim must be positive", nameof(hiddenDim));

            _modelDim = modelDim;
            _hiddenDim = hiddenDim;
            _activation = activation;

            rng ??= new Random(42);

            double scale1 = Math.Sqrt(2.0 / modelDim);
            double scale2 = Math.Sqrt(2.0 / hiddenDim);

            _w1 = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim), rng).Multiply(scale1);
            _w2 = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim), rng).Multiply(scale2);

            _b1 = Tensor.Zeros(new TensorShape3D(1, 1, hiddenDim));
            _b2 = Tensor.Zeros(new TensorShape3D(1, 1, modelDim));
        }

        public Tensor Forward(
            Tensor input,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return FeedForwardSkeleton.Compute(
                input, _w1, _b1, _w2, _b2, _activation, 0.0, progress);
        }

        public Task<Tensor> ForwardAsync(
            Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Forward(input, progress);
            }, cancellationToken);
        }
    }
}
