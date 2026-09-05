namespace CodeRLFramework.RL
{
    public class PolicyGradient
    {
        private readonly Random _rng;

        public double LearningRate { get; set; } = 0.01;

        public double Gamma { get; set; } = 0.99;

        public bool UseBaseline { get; set; } = true;

        public double[] PolicyWeights { get; private set; }

        public double Baseline { get; private set; }

        private List<TrajectoryStep> _currentTrajectory;

        private List<List<TrajectoryStep>> _history;

        public double AverageReward => _history.Count > 0
            ? _history.Average(t => t.Sum(s => s.Reward))
            : 0;

        public double BestAverageReward { get; private set; }

        public PolicyGradient(int actionCount)
        {
            if (actionCount <= 0)
                throw new ArgumentException("动作数量必须大于 0", nameof(actionCount));

            _rng = new Random();
            PolicyWeights = new double[actionCount];
            _currentTrajectory = new List<TrajectoryStep>();
            _history = new List<List<TrajectoryStep>>();
            Baseline = 0;
            BestAverageReward = double.MinValue;
        }

        public int SelectAction(double[] state)
        {
            double[] probs = GetActionProbabilities(state);

            double sample = _rng.NextDouble();
            double cumulative = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                cumulative += probs[i];
                if (sample <= cumulative)
                    return i;
            }

            return probs.Length - 1;
        }

        public double[] GetActionProbabilities(double[] state)
        {
            double[] scores = new double[PolicyWeights.Length];
            for (int i = 0; i < PolicyWeights.Length; i++)
            {
                scores[i] = PolicyWeights[i] * state.Sum();
            }

            return Softmax(scores);
        }

        public void RecordStep(int action, double reward, double[] state)
        {
            _currentTrajectory.Add(new TrajectoryStep
            {
                Action = action,
                Reward = reward,
                State = state,
                LogProb = GetLogProb(action, state)
            });
        }

        public double GetLogProb(int action, double[] state)
        {
            double[] probs = GetActionProbabilities(state);
            double prob = action >= 0 && action < probs.Length ? probs[action] : 0;
            return Math.Log(Math.Max(prob, 1e-10));
        }

        public void EndTrajectory()
        {
            if (_currentTrajectory.Count == 0)
                return;

            var returns = ComputeDiscountedReturns(_currentTrajectory);

            double trajectoryAvgReward = _currentTrajectory.Sum(s => s.Reward);
            Baseline = 0.9 * Baseline + 0.1 * trajectoryAvgReward;

            for (int t = 0; t < _currentTrajectory.Count; t++)
            {
                var step = _currentTrajectory[t];
                double advantage = returns[t] - (UseBaseline ? Baseline : 0);

                double gradient = ComputeGradient(step.Action, step.State, advantage);
                for (int i = 0; i < PolicyWeights.Length; i++)
                {
                    PolicyWeights[i] += LearningRate * gradient;
                }
            }

            _history.Add(_currentTrajectory);
            _currentTrajectory = new List<TrajectoryStep>();

            double avg = AverageReward;
            if (avg > BestAverageReward)
                BestAverageReward = avg;
        }

        private double[] ComputeDiscountedReturns(List<TrajectoryStep> trajectory)
        {
            int T = trajectory.Count;
            double[] returns = new double[T];
            double G = 0;

            for (int t = T - 1; t >= 0; t--)
            {
                G = trajectory[t].Reward + Gamma * G;
                returns[t] = G;
            }

            return returns;
        }

        private double ComputeGradient(int action, double[] state, double advantage)
        {
            double[] probs = GetActionProbabilities(state);

            double gradient = 0;
            for (int i = 0; i < PolicyWeights.Length; i++)
            {
                if (i == action)
                    gradient += (1 - probs[i]) * advantage;
                else
                    gradient += -probs[i] * advantage;
            }

            return gradient;
        }

        private static double[] Softmax(double[] scores)
        {
            double max = scores.Max();
            var exp = scores.Select(s => Math.Exp(s - max)).ToArray();
            double sum = exp.Sum();
            return sum > 0 ? exp.Select(e => e / sum).ToArray() : new double[scores.Length];
        }

        public void Reset()
        {
            Array.Clear(PolicyWeights, 0, PolicyWeights.Length);
            _currentTrajectory.Clear();
            _history.Clear();
            Baseline = 0;
            BestAverageReward = double.MinValue;
        }
    }

    public class TrajectoryStep
    {
        public int Action { get; set; }

        public double Reward { get; set; }

        public double[] State { get; set; } = Array.Empty<double>();

        public double LogProb { get; set; }
    }
}
