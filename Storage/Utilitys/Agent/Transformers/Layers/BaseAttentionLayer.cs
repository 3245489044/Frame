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
    public abstract class BaseAttentionLayer
    {
        protected readonly int _numHeads;
        protected readonly int _headDim;
        protected readonly int _modelDim;

        protected readonly Tensor _wq, _wk, _wv, _wo;
        protected readonly Tensor _bq, _bk, _bv, _bo;
        
        internal int ModelDim => _modelDim;
        internal int NumHeads => _numHeads;
        internal int HeadDim => _headDim;
        internal Tensor Wq => _wq;
        internal Tensor Wk => _wk;
        internal Tensor Wv => _wv;
        internal Tensor Wo => _wo;
        internal Tensor Bq => _bq;
        internal Tensor Bk => _bk;
        internal Tensor Bv => _bv;
        internal Tensor Bo => _bo;

        protected BaseAttentionLayer(
            int modelDim,
            int numHeads,
            Random? rng = null)
        {
            if (modelDim <= 0)
                throw new ArgumentException("modelDim must be positive", nameof(modelDim));
            if (numHeads <= 0)
                throw new ArgumentException("numHeads must be positive", nameof(numHeads));
            if (modelDim % numHeads != 0)
                throw new ArgumentException(
                    $"modelDim ({modelDim}) must be divisible by numHeads ({numHeads})",
                    nameof(modelDim));

            _modelDim = modelDim;
            _numHeads = numHeads;
            _headDim = modelDim / numHeads;

            rng ??= new Random(42);

            double scale = Math.Sqrt(2.0 / modelDim);
            var shape = new TensorShape3D(1, modelDim, modelDim);

            _wq = Tensor.Random(shape, rng).Multiply(scale);
            _wk = Tensor.Random(shape, rng).Multiply(scale);
            _wv = Tensor.Random(shape, rng).Multiply(scale);
            _wo = Tensor.Random(shape, rng).Multiply(scale);

            var biasShape = new TensorShape3D(1, 1, modelDim);
            _bq = Tensor.Zeros(biasShape);
            _bk = Tensor.Zeros(biasShape);
            _bv = Tensor.Zeros(biasShape);
            _bo = Tensor.Zeros(biasShape);
        }

        protected (Tensor Q, Tensor K, Tensor V) ProjectQKV(Tensor input)
        {
            var Q = input.MatMul(_wq).Add(_bq);
            var K = input.MatMul(_wk).Add(_bk);
            var V = input.MatMul(_wv).Add(_bv);
            return (Q, K, V);
        }

        protected (Tensor[] QHeads, Tensor[] KHeads, Tensor[] VHeads) SplitHeads(Tensor Q, Tensor K, Tensor V)
        {
            return (
                TensorOperations.SplitHeads(Q, _numHeads),
                TensorOperations.SplitHeads(K, _numHeads),
                TensorOperations.SplitHeads(V, _numHeads));
        }

        protected Tensor[] ComputeHeads(
            Tensor[] qHeads,
            Tensor[] kHeads,
            Tensor[] vHeads,
            Tensor? mask,
            IProgress<ComputationProgress>? progress = null)
        {
            var headOutputs = new Tensor[_numHeads];
            for (int h = 0; h < _numHeads; h++)
            {
                headOutputs[h] = AttentionSkeleton.Compute(
                    qHeads[h], kHeads[h], vHeads[h], mask, progress);
            }
            return headOutputs;
        }

        protected Tensor ConcatAndProject(Tensor[] headOutputs)
        {
            var concat = TensorOperations.ConcatHeads(headOutputs);
            return concat.MatMul(_wo).Add(_bo);
        }

        protected Task<Tensor> ForwardAsync(
            Func<Tensor?, IProgress<ComputationProgress>?, Tensor> forward,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return forward(mask, progress);
            }, cancellationToken);
        }
    }
}
