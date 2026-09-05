#region USINGS
using System;
#endregion

namespace Agent.Transformers.Core
{
    public enum AttentionType
    {
        MHA = 0,

        GQA = 1,

        MQA = 2
    }

    public enum ActivationType
    {
        ReLU = 0,

        GELU = 1,

        Swish = 2,

        Tanh = 3,

        Sigmoid = 4
    }

    public enum FittingType
    {
        LeastSquares = 0,

        GradientFree = 1,

        Online = 2,

        LayerwiseBP = 3,

        OnlineBackprop = 4,

        PolicyGradient = 5,

        PPO = 6
    }

    public enum OutputType
    {
        Regression = 0,

        Classification = 1,

        Sequence = 2
    }

    public enum NormalizationType
    {
        ZScore,

        MinMax
    }

    public enum PoolingStrategy
    {
        Mean,

        Max,

        First,

        Last
    }
}
