#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Models;
using Agent.Transformers.Runtime;
using Agent.AgentFlow.Models;
#endregion

namespace Agent.AgentFlow.Config
{
    public record AgentFlowConfig
    {
        #region 模型架构

        public int ModelDim { get; init; } = 64;

        public int NumLayers { get; init; } = 2;

        public int NumHeads { get; init; } = 4;

        public int FfnHiddenDim => HiddenDim ?? 4 * ModelDim;

        public int? HiddenDim { get; init; } = null;

        public Agent.Transformers.Core.ActivationType Activation { get; init; } = Agent.Transformers.Core.ActivationType.GELU;

        public bool PreNorm { get; init; } = true;

        public double LayerNormEps { get; init; } = 1e-5;

        #endregion

        #region 文本生成配置

        public int VocabSize { get; init; } = 50257;

        public int MaxSeqLen { get; init; } = 1024;

        public bool UseRoPE { get; init; } = true;

        public double RoPETheta { get; init; } = 10000.0;

        public bool UseKVCache { get; init; } = true;

        public AttentionType AttentionType { get; init; } = AttentionType.MHA;

        public int NumKVHeads { get; init; } = 0;

        public double DropoutRate { get; init; } = 0.1;

        public int EosTokenId { get; init; } = 0;

        public double DefaultTemperature { get; init; } = 1.0;

        public int DefaultMaxNewTokens { get; init; } = 50;

        public int? Seed { get; init; } = 42;

        #endregion

        #region 运行时拟合配置

        public FittingType FittingStrategy { get; init; } = FittingType.LeastSquares;

        public int MaxIterations { get; init; } = 100;

        public double ConvergenceThreshold { get; init; } = 1e-6;

        public double RegularizationLambda { get; init; } = 0.01;

        public bool AutoNormalize { get; init; } = true;

        #endregion

        #region 输出配置

        public OutputType OutputType { get; init; } = OutputType.Regression;

        public int NumClasses { get; init; } = 2;

        public int OutputSeqLen { get; init; } = 1;

        #endregion

        #region 运行模式

        public AgentMode Mode { get; init; } = AgentMode.TextGeneration;

        #endregion

        #region 预设配置

        public static AgentFlowConfig Default => new();

        public static AgentFlowConfig Fast => new()
        {
            ModelDim = 32,
            NumLayers = 1,
            NumHeads = 2,
            MaxIterations = 50,
            Mode = AgentMode.Regression
        };

        public static AgentFlowConfig Precise => new()
        {
            ModelDim = 128,
            NumLayers = 4,
            NumHeads = 8,
            MaxIterations = 500,
            ConvergenceThreshold = 1e-8,
            Mode = AgentMode.Regression
        };

        public static AgentFlowConfig Gpt2Small => new()
        {
            ModelDim = 768,
            NumHeads = 12,
            NumLayers = 12,
            MaxSeqLen = 1024,
            VocabSize = 50257,
            Mode = AgentMode.TextGeneration
        };

        public static AgentFlowConfig BertBase => new()
        {
            ModelDim = 768,
            NumHeads = 12,
            NumLayers = 12,
            MaxSeqLen = 512,
            VocabSize = 30522,
            PreNorm = false,
            Mode = AgentMode.FeatureExtraction
        };

        #endregion

        #region 转换方法

        public TransformerConfig ToTransformerConfig() => new()
        {
            VocabSize = VocabSize,
            MaxSeqLen = MaxSeqLen,
            ModelDim = ModelDim,
            NumHeads = NumHeads,
            NumLayers = NumLayers,
            HiddenDim = HiddenDim,
            DropoutRate = DropoutRate,
            LayerNormEps = LayerNormEps,
            PreNorm = PreNorm,
            UseRoPE = UseRoPE,
            RoPETheta = RoPETheta,
            UseKVCache = UseKVCache,
            AttentionType = AttentionType,
            NumKVHeads = NumKVHeads,
            Activation = Activation,
            EosTokenId = EosTokenId,
            DefaultTemperature = DefaultTemperature,
            DefaultMaxNewTokens = DefaultMaxNewTokens,
            Seed = Seed
        };

        public RuntimeConfig ToRuntimeConfig() => new()
        {
            ModelDim = ModelDim,
            NumLayers = NumLayers,
            NumHeads = NumHeads,
            HiddenDim = HiddenDim,
            Activation = Activation,
            PreNorm = PreNorm,
            LayerNormEps = LayerNormEps,
            FittingStrategy = FittingStrategy,
            MaxIterations = MaxIterations,
            ConvergenceThreshold = ConvergenceThreshold,
            RegularizationLambda = RegularizationLambda,
            OutputType = OutputType,
            NumClasses = NumClasses,
            OutputSeqLen = OutputSeqLen,
            AutoNormalize = AutoNormalize,
            UsePositionEncoding = UseRoPE,
            MaxSeqLen = MaxSeqLen
        };

        public static AgentFlowConfig FromTransformerConfig(TransformerConfig config) => new()
        {
            ModelDim = config.ModelDim,
            NumLayers = config.NumLayers,
            NumHeads = config.NumHeads,
            HiddenDim = config.HiddenDim,
            Activation = config.Activation,
            PreNorm = config.PreNorm,
            LayerNormEps = config.LayerNormEps,
            VocabSize = config.VocabSize,
            MaxSeqLen = config.MaxSeqLen,
            UseRoPE = config.UseRoPE,
            RoPETheta = config.RoPETheta,
            UseKVCache = config.UseKVCache,
            AttentionType = config.AttentionType,
            NumKVHeads = config.NumKVHeads,
            DropoutRate = config.DropoutRate,
            EosTokenId = config.EosTokenId,
            DefaultTemperature = config.DefaultTemperature,
            DefaultMaxNewTokens = config.DefaultMaxNewTokens,
            Seed = config.Seed,
            Mode = AgentMode.TextGeneration
        };

        public static AgentFlowConfig FromRuntimeConfig(RuntimeConfig config) => new()
        {
            ModelDim = config.ModelDim,
            NumLayers = config.NumLayers,
            NumHeads = config.NumHeads,
            HiddenDim = config.HiddenDim,
            Activation = config.Activation,
            PreNorm = config.PreNorm,
            LayerNormEps = config.LayerNormEps,
            MaxSeqLen = config.MaxSeqLen,
            FittingStrategy = config.FittingStrategy,
            MaxIterations = config.MaxIterations,
            ConvergenceThreshold = config.ConvergenceThreshold,
            RegularizationLambda = config.RegularizationLambda,
            OutputType = config.OutputType,
            NumClasses = config.NumClasses,
            OutputSeqLen = config.OutputSeqLen,
            AutoNormalize = config.AutoNormalize,
            UseRoPE = config.UsePositionEncoding,
            Mode = AgentMode.Regression
        };

        #endregion
    }

}
