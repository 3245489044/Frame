#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class RegressionHead
    {
        public Tensor? Weight { get; set; }

        public Tensor? Bias { get; set; }

        public int OutputDim { get; }

        public int ModelDim { get; }

        public RegressionHead(int modelDim, int outputDim)
        {
            ModelDim = modelDim;
            OutputDim = outputDim;
        }

        public Tensor Forward(Tensor features)
        {
            if (Weight == null)
                throw new InvalidOperationException("RegressionHead not initialized.");

            var output = features.MatMul(Weight!);

            if (Bias != null)
            {
                for (int b = 0; b < output.BatchSize; b++)
                    for (int d = 0; d < output.Dimension; d++)
                        output[b, 0, d] += Bias[0, 0, d];
            }

            return output;
        }
    }
}
