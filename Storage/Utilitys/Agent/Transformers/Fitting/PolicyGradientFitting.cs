#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Runtime
{
    public class PolicyGradientFitting : BaseFittingStrategy
    {
        #region 属性

        public override string Name => "PolicyGradient (REINFORCE)";
        public override FittingType FittingType => FittingType.PolicyGradient;

        public double Gamma { get; set; } = 0.99;

        public double PolicyLearningRate { get; set; } = 0.01;

        public double ValueLearningRate { get; set; } = 0.005;

        public bool UpdateOnEpisodeEnd { get; set; } = true;

        #endregion

        #region 核心方法

        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            int modelDim = context.Config.ModelDim;
            int outputDim = context.OutputDim;

            context.OutputWeight ??= new Tensor(new TensorShape3D(1, modelDim, outputDim));
            context.OutputBias ??= new Tensor(new TensorShape3D(1, 1, outputDim));

            context.ValueWeight ??= new Tensor(new TensorShape3D(1, modelDim, 1));
            context.ValueBias ??= new Tensor(new TensorShape3D(1, 1, 1));

            context.Trajectory ??= new List<TrajectoryStep>();

            return true;
        }

        public override Tensor Predict(RuntimeContext context, Tensor X)
        {
            if (context.OutputWeight == null)
                throw new InvalidOperationException("Policy network not initialized. Call Fit() first.");

            var logits = X.MatMul(context.OutputWeight);
            if (context.OutputBias != null)
            {
                for (int i = 0; i < logits.BatchSize; i++)
                    for (int j = 0; j < logits.Dimension; j++)
                        logits[i, 0, j] += context.OutputBias[0, 0, j];
            }
            return logits;
        }

        public (int action, double logProb) SampleAction(RuntimeContext context, Tensor logits, double temperature = 1.0)
        {
            int numActions = logits.Dimension;

            var scaled = new double[numActions];
            for (int i = 0; i < numActions; i++)
                scaled[i] = logits[0, 0, i] / temperature;

            var probs = Softmax(scaled);

            var random = new Random();
            double cumulative = 0;
            double sample = random.NextDouble();
            int action = numActions - 1;

            for (int i = 0; i < numActions; i++)
            {
                cumulative += probs[i];
                if (sample < cumulative)
                {
                    action = i;
                    break;
                }
            }

            double logProb = Math.Log(probs[action] + 1e-10);

            return (action, logProb);
        }

        public void RecordStep(RuntimeContext context, Tensor state, int action, double logProb, double reward, bool done, double value)
        {
            context.Trajectory ??= new List<TrajectoryStep>();

            context.Trajectory.Add(new TrajectoryStep
            {
                State = state,
                Action = action,
                LogProb = logProb,
                Reward = reward,
                Done = done,
                Value = value
            });

            context.RewardHistory.Add(reward);
            context.EpisodeReturn += reward;

            if (done)
            {
                context.EpisodeCount++;
                context.EpisodeReturn = 0;
            }
        }

        public double[] ComputeMonteCarloReturns(RuntimeContext context)
        {
            var trajectory = context.Trajectory;
            if (trajectory == null || trajectory.Count == 0)
                return Array.Empty<double>();

            int T = trajectory.Count;
            var returns = new double[T];
            double G = 0;

            for (int t = T - 1; t >= 0; t--)
            {
                G = trajectory[t].Reward + Gamma * G;
                returns[t] = G;
            }

            return returns;
        }

        public double[] ComputeAdvantages(RuntimeContext context)
        {
            var trajectory = context.Trajectory;
            if (trajectory == null || trajectory.Count == 0)
                return Array.Empty<double>();

            var returns = ComputeMonteCarloReturns(context);
            var advantages = new double[returns.Length];

            for (int t = 0; t < returns.Length; t++)
            {
                advantages[t] = returns[t] - trajectory[t].Value;
            }

            return advantages;
        }

        public void UpdateFromTrajectory(RuntimeContext context)
        {
            var trajectory = context.Trajectory;
            if (trajectory == null || trajectory.Count == 0)
                return;

            var advantages = ComputeAdvantages(context);
            var operator_ = new PolicyGradientOperator();

            for (int t = 0; t < trajectory.Count; t++)
            {
                var state = trajectory[t].State;
                if (state == null) continue;

                var advantageTensor = new Tensor(new TensorShape3D(1, 1, 1));
                advantageTensor[0, 0, 0] = advantages[t];

                operator_.Update(context, state, advantageTensor, PolicyLearningRate);
            }

            context.Trajectory.Clear();
        }

        #endregion

        #region 辅助方法

        private static double[] Softmax(double[] logits)
        {
            int n = logits.Length;
            var result = new double[n];
            double max = double.NegativeInfinity;

            for (int i = 0; i < n; i++)
                if (logits[i] > max) max = logits[i];

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                result[i] = Math.Exp(logits[i] - max);
                sum += result[i];
            }

            for (int i = 0; i < n; i++)
                result[i] /= sum;

            return result;
        }

        #endregion
    }
}
