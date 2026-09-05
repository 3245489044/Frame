#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class ClassificationHead
    {
        public Tensor? Weight { get; set; }

        public Tensor? Bias { get; set; }

        public int NumClasses { get; }

        public int ModelDim { get; }

        public ClassificationHead(int modelDim, int numClasses)
        {
            ModelDim = modelDim;
            NumClasses = numClasses;
        }

        public Tensor Forward(Tensor features)
        {
            if (Weight == null)
                throw new InvalidOperationException("ClassificationHead not initialized.");

            var logits = features.MatMul(Weight!);

            if (Bias != null)
            {
                for (int b = 0; b < logits.BatchSize; b++)
                    for (int d = 0; d < logits.Dimension; d++)
                        logits[b, 0, d] += Bias[0, 0, d];
            }

            return logits;
        }

        public Tensor ForwardSoftmax(Tensor features)
        {
            var logits = Forward(features);
            return logits.Softmax(dim: 2);
        }

        public int[] PredictClass(Tensor features)
        {
            var probs = ForwardSoftmax(features);
            int batchSize = probs.BatchSize;
            var predictions = new int[batchSize];

            for (int b = 0; b < batchSize; b++)
            {
                double maxProb = double.MinValue;
                int maxIdx = 0;
                for (int d = 0; d < probs.Dimension; d++)
                {
                    if (probs[b, 0, d] > maxProb)
                    {
                        maxProb = probs[b, 0, d];
                        maxIdx = d;
                    }
                }
                predictions[b] = maxIdx;
            }

            return predictions;
        }
    }
}
