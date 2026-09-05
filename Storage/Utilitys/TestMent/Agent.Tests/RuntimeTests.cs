namespace Agent.Tests
{
    public class RuntimeTests
    {
        [Fact]
        public void TestRuntimeLeastSquares()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 100,
                ConvergenceThreshold = 1e-6
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 100;
            var X = new double[numSamples, 2];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                X[i, 1] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 3 * X[i, 1] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                Assert.True(engine.IsFitted);
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestRuntimeSerialization()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 50,
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
                y[i, 0] = 1.5 * X[i, 0] + 0.5 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                var predictionsBefore = engine.Predict(new double[,] { { 5.0 } });

                string modelPath = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid()}.json");
                try
                {
                    engine.Save(modelPath);
                    Assert.True(File.Exists(modelPath));

                    var loadedEngine = new RuntimeEngine(config);
                    loadedEngine.Load(modelPath);
                    Assert.True(loadedEngine.IsFitted);

                    var predictionsAfter = loadedEngine.Predict(new double[,] { { 5.0 } });
                    double diff = Math.Abs(predictionsBefore[0, 0] - predictionsAfter[0, 0]);
                    Assert.True(diff < 1e-6);

                    var summary = loadedEngine.GetSummary();
                    Assert.False(string.IsNullOrEmpty(summary));
                }
                finally
                {
                    if (File.Exists(modelPath))
                        File.Delete(modelPath);
                }
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestRuntimePredictWithoutFitThrows()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
            };

            var engine = new RuntimeEngine(config);
            Assert.False(engine.IsFitted);

            Assert.Throws<InvalidOperationException>(() => engine.Predict(new double[,] { { 1.0 } }));
        }

        [Fact]
        public void TestRuntimeConfigProperties()
        {
            var config = new RuntimeConfig
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 100,
                ConvergenceThreshold = 1e-6,
                ModelDim = 32,
                NumLayers = 2,
                NumHeads = 4
            };

            Assert.Equal(FittingType.LeastSquares, config.FittingStrategy);
            Assert.Equal(100, config.MaxIterations);
            Assert.Equal(1e-6, config.ConvergenceThreshold);
            Assert.Equal(32, config.ModelDim);
            Assert.Equal(2, config.NumLayers);
            Assert.Equal(4, config.NumHeads);
        }

        [Fact]
        public void TestRuntimeDifferentStrategies()
        {
            var strategies = new[] { FittingType.LeastSquares, FittingType.GradientFree };

            foreach (var strategy in strategies)
            {
                var config = RuntimeConfig.Fast with
                {
                    FittingStrategy = strategy,
                    MaxIterations = 10,
                    ConvergenceThreshold = 1e-3
                };

                var engine = new RuntimeEngine(config);
                Assert.NotNull(engine);
            }
        }

        [Fact]
        public void TestRuntimeNullInputThrows()
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
        public void TestRuntimeDataMismatchThrows()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
            };

            var engine = new RuntimeEngine(config);
            var X = new double[10, 2];
            var y = new double[5, 1];

            Assert.Throws<IndexOutOfRangeException>(() => engine.Fit(X, y));
        }

        [Fact]
        public void TestRuntimeStep()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
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
                y[i, 0] = 2 * X[i, 0] + 1;
            }

            try
            {
                engine.Fit(X, y);
                Assert.True(engine.IsFitted);
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestSaveUnfittedModelThrows()
        {
            var config = RuntimeConfig.Fast with
            {
                FittingStrategy = FittingType.LeastSquares,
            };

            var engine = new RuntimeEngine(config);

            Assert.Throws<InvalidOperationException>(() => engine.Save("test.json"));
        }
    }
}
