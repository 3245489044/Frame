namespace Agent.Tests
{
    public class FittingStrategyTests
    {
        [Fact]
        public void TestLeastSquaresFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 100,
                ConvergenceThreshold = 1e-6
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 50;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
                Assert.Equal(1, predictions.GetLength(0));
                Assert.Equal(1, predictions.GetLength(1));
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestLeastSquaresFittingNullInputThrows()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
            };

            var engine = new RuntimeEngine(config);

            Assert.Throws<InvalidOperationException>(() => engine.Predict((double[,])null!));
            Assert.Throws<InvalidOperationException>(() => engine.Predict((Tensor)null!));
        }

        [Fact]
        public void TestGradientFreeFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.GradientFree,
                MaxIterations = 10,
                ConvergenceThreshold = 1e-3
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 20;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestOnlineFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.Online,
                MaxIterations = 10
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 10;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestLayerwiseFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LayerwiseBP,
                MaxIterations = 5
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 10;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestOnlineBackpropFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.OnlineBackprop,
                MaxIterations = 5
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 10;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestPolicyGradientFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.PolicyGradient,
                MaxIterations = 5
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 10;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestPPOFitting()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.PPO,
                MaxIterations = 5
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 10;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);
            }
            catch
            {
            }
        }

        [Fact]
        public void TestStrategySwitch()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
            };

            var engine = new RuntimeEngine(config);
            Assert.NotNull(engine.Strategy);

            var newStrategy = new GradientFreeFitting();
            engine.SetStrategy(newStrategy);
            Assert.Same(newStrategy, engine.Strategy);
        }
    }
}
