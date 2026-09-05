#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public record RuntimeConfig
    {
        #region 模型架构

        public int ModelDim { get; init; } = 64;

        public int NumLayers { get; init; } = 2;

        public int NumHeads { get; init; } = 4;

        public int FfnHiddenDim => HiddenDim ?? 4 * ModelDim;

        public int? HiddenDim { get; init; } = null;

        public Core.ActivationType Activation { get; init; } = Core.ActivationType.GELU;

        public bool PreNorm { get; init; } = true;

        public double LayerNormEps { get; init; } = 1e-5;

        #endregion

        #region 拟合策略

        public FittingType FittingStrategy { get; init; } = FittingType.LeastSquares;

        public int MaxIterations { get; init; } = 100;

        public double ConvergenceThreshold { get; init; } = 1e-6;

        public double RegularizationLambda { get; init; } = 0.01;

        #endregion

        #region 输出配置

        public OutputType OutputType { get; init; } = OutputType.Regression;

        public int NumClasses { get; init; } = 2;

        public int OutputSeqLen { get; init; } = 1;

        #endregion

        #region 数据预处理

        public bool AutoNormalize { get; init; } = true;

        public bool UsePositionEncoding { get; init; } = true;

        public int MaxSeqLen { get; init; } = 512;

        #endregion

        #region 预设配置

        public static RuntimeConfig Default => new();

        public static RuntimeConfig Fast => new()
        {
            ModelDim = 32,
            NumLayers = 1,
            NumHeads = 2,
            MaxIterations = 50
        };

        public static RuntimeConfig Precise => new()
        {
            ModelDim = 128,
            NumLayers = 4,
            NumHeads = 8,
            MaxIterations = 500,
            ConvergenceThreshold = 1e-8
        };

        public static RuntimeConfig Classification(int numClasses) => new()
        {
            OutputType = OutputType.Classification,
            NumClasses = numClasses,
            FittingStrategy = FittingType.GradientFree
        };

        public static RuntimeConfig TimeSeries(int forecastLen) => new()
        {
            OutputType = OutputType.Sequence,
            OutputSeqLen = forecastLen,
            FittingStrategy = FittingType.Online
        };

        #endregion
    }

}
