#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Models
{
    public record TransformerConfig
    {
        public int VocabSize { get; init; } = 50257;

        public int MaxSeqLen { get; init; } = 1024;

        public int ModelDim { get; init; } = 768;

        public int NumHeads { get; init; } = 12;

        public int NumLayers { get; init; } = 12;

        public int FfnHiddenDim => HiddenDim ?? 4 * ModelDim;

        public int? HiddenDim { get; init; } = null;

        public double DropoutRate { get; init; } = 0.1;

        public double LayerNormEps { get; init; } = 1e-5;

        public bool PreNorm { get; init; } = true;

        public bool UseRoPE { get; init; } = true;

        public double RoPETheta { get; init; } = 10000.0;

        public bool UseKVCache { get; init; } = true;

        public AttentionType AttentionType { get; init; } = AttentionType.MHA;

        public int NumKVHeads { get; init; } = 0;

        public Core.ActivationType Activation { get; init; } = Core.ActivationType.GELU;

        public int EosTokenId { get; init; } = 0;

        public double DefaultTemperature { get; init; } = 1.0;

        public int DefaultMaxNewTokens { get; init; } = 50;

        public int? Seed { get; init; } = 42;

        public static TransformerConfig Default => new();

        public static TransformerConfig Gpt2Medium => new()
        {
            ModelDim = 1024,
            NumHeads = 16,
            NumLayers = 24,
            MaxSeqLen = 1024
        };

        public static TransformerConfig Gpt2Large => new()
        {
            ModelDim = 1280,
            NumHeads = 20,
            NumLayers = 36,
            MaxSeqLen = 1024
        };

        public static TransformerConfig BertBase => new()
        {
            VocabSize = 30522,
            ModelDim = 768,
            NumHeads = 12,
            NumLayers = 12,
            MaxSeqLen = 512,
            PreNorm = false
        };
    }
}
