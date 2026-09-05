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
    public class TransformerBlock
    {
        private readonly MultiHeadAttentionLayer _attention;
        private readonly FeedForwardLayer _ffn;

        private readonly double[] _gamma1, _beta1;
        private readonly double[] _gamma2, _beta2;

        internal MultiHeadAttentionLayer Attention => _attention;

        internal FeedForwardLayer FFN => _ffn;

        internal double[] Gamma1 => _gamma1;

        internal double[] Beta1 => _beta1;

        internal double[] Gamma2 => _gamma2;

        internal double[] Beta2 => _beta2;

        public double[] GetParameters()
        {
            int d = _attention.ModelDim;
            int h = _ffn.HiddenDim;
            int totalParams = GetParameterCount();

            var result = new double[totalParams];
            int idx = 0;

            CopyTensorParams(_attention.Wq, result, ref idx);
            CopyTensorParams(_attention.Wk, result, ref idx);
            CopyTensorParams(_attention.Wv, result, ref idx);
            CopyTensorParams(_attention.Wo, result, ref idx);
            CopyTensorParams(_attention.Bq, result, ref idx);
            CopyTensorParams(_attention.Bk, result, ref idx);
            CopyTensorParams(_attention.Bv, result, ref idx);
            CopyTensorParams(_attention.Bo, result, ref idx);

            CopyTensorParams(_ffn.W1, result, ref idx);
            CopyTensorParams(_ffn.W2, result, ref idx);
            CopyTensorParams(_ffn.B1, result, ref idx);
            CopyTensorParams(_ffn.B2, result, ref idx);

            Array.Copy(_gamma1, 0, result, idx, d); idx += d;
            Array.Copy(_beta1, 0, result, idx, d); idx += d;
            Array.Copy(_gamma2, 0, result, idx, d); idx += d;
            Array.Copy(_beta2, 0, result, idx, d); idx += d;

            return result;
        }

        public void SetParameters(double[] parameters)
        {
            int idx = 0;
            int d = _attention.ModelDim;

            LoadTensorParams(_attention.Wq, parameters, ref idx);
            LoadTensorParams(_attention.Wk, parameters, ref idx);
            LoadTensorParams(_attention.Wv, parameters, ref idx);
            LoadTensorParams(_attention.Wo, parameters, ref idx);
            LoadTensorParams(_attention.Bq, parameters, ref idx);
            LoadTensorParams(_attention.Bk, parameters, ref idx);
            LoadTensorParams(_attention.Bv, parameters, ref idx);
            LoadTensorParams(_attention.Bo, parameters, ref idx);

            LoadTensorParams(_ffn.W1, parameters, ref idx);
            LoadTensorParams(_ffn.W2, parameters, ref idx);
            LoadTensorParams(_ffn.B1, parameters, ref idx);
            LoadTensorParams(_ffn.B2, parameters, ref idx);

            Array.Copy(parameters, idx, _gamma1, 0, d); idx += d;
            Array.Copy(parameters, idx, _beta1, 0, d); idx += d;
            Array.Copy(parameters, idx, _gamma2, 0, d); idx += d;
            Array.Copy(parameters, idx, _beta2, 0, d); idx += d;
        }

        private static void CopyTensorParams(Tensor t, double[] dest, ref int idx)
        {
            for (int i = 0; i < t.BatchSize; i++)
                for (int j = 0; j < t.SeqLength; j++)
                    for (int k = 0; k < t.Dimension; k++)
                        dest[idx++] = t[i, j, k];
        }

        private static void LoadTensorParams(Tensor t, double[] src, ref int idx)
        {
            for (int i = 0; i < t.BatchSize; i++)
                for (int j = 0; j < t.SeqLength; j++)
                    for (int k = 0; k < t.Dimension; k++)
                        t[i, j, k] = src[idx++];
        }

        public int GetParameterCount()
        {
            int d = _attention.ModelDim;
            int h = _ffn.HiddenDim;
            return 4 * d * d + 4 * d
                 + d * h + h * d + h + d
                 + 4 * d;
        }

        public TransformerBlock(
            int modelDim,
            int numHeads,
            int ffnHiddenDim,
            Core.ActivationType activation = Core.ActivationType.GELU,
            Random? rng = null)
        {
            _attention = new MultiHeadAttentionLayer(modelDim, numHeads, rng);
            _ffn = new FeedForwardLayer(modelDim, ffnHiddenDim, activation, rng);

            (_gamma1, _beta1) = NormalizationSkeleton.CreateLayerNormParams(modelDim);
            (_gamma2, _beta2) = NormalizationSkeleton.CreateLayerNormParams(modelDim);

        }

        public Tensor Forward(
            Tensor input,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            progress?.Report(new ComputationProgress(0, "Block: Step 1/2 - Self-Attention"));

            var attnOutput = ResidualSkeleton.Apply(
                input,
                (x) => _attention.Forward(x, mask),
                _gamma1, _beta1,
                preNorm: true);

            progress?.Report(new ComputationProgress(50, "Block: Step 2/2 - Feed-Forward"));

            var output = ResidualSkeleton.Apply(
                attnOutput,
                (x) => _ffn.Forward(x),
                _gamma2, _beta2,
                preNorm: true);

            progress?.Report(new ComputationProgress(100, "Block: Complete"));

            return output;
        }

        public Tensor ForwardWithAttentionWeights(
            Tensor input,
            out Tensor attentionWeights,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            progress?.Report(new ComputationProgress(0, "Block: Step 1/2 - Self-Attention (with weights)"));

            Tensor capturedWeights = null!;
            var attnOutput = ResidualSkeleton.Apply(
                input,
                (x) =>
                {
                    var result = _attention.ForwardWithAttentionWeights(
                        x, out var attnW, mask);
                    capturedWeights = attnW;
                    return result;
                },
                _gamma1, _beta1,
                preNorm: true);
            attentionWeights = capturedWeights;

            progress?.Report(new ComputationProgress(50, "Block: Step 2/2 - Feed-Forward"));

            var output = ResidualSkeleton.Apply(
                attnOutput,
                (x) => _ffn.Forward(x),
                _gamma2, _beta2,
                preNorm: true);

            progress?.Report(new ComputationProgress(100, "Block: Complete"));

            return output;
        }




        public Tensor ForwardWithCache(
            Tensor input,
            KVCache kvCache,
            int layerIndex,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (kvCache == null)
                throw new ArgumentNullException(nameof(kvCache));

            var attnOutput = ResidualSkeleton.Apply(
                input,
                (x) => _attention.ForwardWithCache(x, kvCache, layerIndex, mask),
                _gamma1, _beta1,
                preNorm: true);

            var output = ResidualSkeleton.Apply(
                attnOutput,
                (x) => _ffn.Forward(x),
                _gamma2, _beta2,
                preNorm: true);

            return output;
        }

        public Task<Tensor> ForwardAsync(
            Tensor input,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Forward(input, mask, progress);
            }, cancellationToken);
        }
    }
}
