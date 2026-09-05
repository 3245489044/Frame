#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Runtime
{
    public class RuntimeContext
    {
        #region 配置

        public RuntimeConfig Config { get; }

        public int InputDim { get; set; }

        public int OutputDim { get; set; }

        #endregion

        #region 归一化参数

        public double[]? InputMean { get; set; }

        public double[]? InputStd { get; set; }

        public double[]? OutputMean { get; set; }

        public double[]? OutputStd { get; set; }

        #endregion

        #region 模型权重

        public List<TransformerBlock>? Blocks { get; set; }

        public Tensor? TokenEmbedding { get; set; }

        public Tensor? PositionEmbedding { get; set; }

        public Tensor? OutputWeight { get; set; }

        public Tensor? OutputBias { get; set; }

        public double[]? GammaFinal { get; set; }

        public double[]? BetaFinal { get; set; }

        #endregion

        #region 强化学习（RL）状态

        public Tensor? ValueWeight { get; set; }

        public Tensor? ValueBias { get; set; }

        public List<TrajectoryStep>? Trajectory { get; set; }

        public List<double> RewardHistory { get; } = new();

        public double EpisodeReturn { get; set; }

        public int EpisodeCount { get; set; }

        public Tensor? OldActionProbs { get; set; }

        #endregion

        #region 拟合历史

        public List<double> LossHistory { get; } = new();

        public bool IsFitted { get; set; }

        public long FittingTimeMs { get; set; }

        public double FinalLoss { get; set; }

        public int NumSamples { get; set; }

        #endregion

        public RuntimeContext(RuntimeConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Reset()
        {
            InputMean = null;
            InputStd = null;
            OutputMean = null;
            OutputStd = null;
            Blocks = null;
            TokenEmbedding = null;
            PositionEmbedding = null;
            OutputWeight = null;
            OutputBias = null;
            GammaFinal = null;
            BetaFinal = null;
            ValueWeight = null;
            ValueBias = null;
            Trajectory = null;
            OldActionProbs = null;
            RewardHistory.Clear();
            LossHistory.Clear();
            IsFitted = false;
            FittingTimeMs = 0;
            FinalLoss = 0;
            NumSamples = 0;
            EpisodeReturn = 0;
            EpisodeCount = 0;
        }

        public string GetSummary()
        {
            if (!IsFitted)
                return "RuntimeContext: Not fitted";

            string rlInfo = "";
            if (ValueWeight != null)
            {
                rlInfo = $", episodes={EpisodeCount}, avg_return={(EpisodeCount > 0 ? (EpisodeReturn / EpisodeCount):0):F4}";
            }

            return $"RuntimeContext: Fitted ({NumSamples} samples, {FittingTimeMs}ms, " +
                   $"final loss={FinalLoss:F6}, " +
                   $"blocks={Blocks?.Count ?? 0}, " +
                   $"input_dim={InputDim}, output_dim={OutputDim}" +
                   rlInfo + ")";
        }
    }

    public class TrajectoryStep
    {
        public Tensor? State { get; set; }

        public int Action { get; set; }

        public double LogProb { get; set; }

        public double Reward { get; set; }

        public bool Done { get; set; }

        public double Value { get; set; }
    }
}
