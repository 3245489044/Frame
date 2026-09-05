using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Registry
{
    [TestClass]
    public class StrategyRegistryTests : TestBase
    {
        [TestMethod]
        public void RunAll_StrategyRegistryTests()
        {
            var result = RunTestGroup("StrategyRegistry", builder =>
            {
                builder.Add("Register_ShouldAddStrategy", () =>
                {
                    var registry = new StrategyRegistry();
                    var strategy = new SequentialExecutionStrategy();
                    registry.Register("sequential", strategy);

                    Assert.IsTrue(registry.HasStrategy("sequential"));
                });

                builder.Add("Register_DuplicateName_ShouldThrow", () =>
                {
                    var registry = new StrategyRegistry();
                    registry.Register("sequential", new SequentialExecutionStrategy());

                    Assert.ThrowsException<ArgumentException>(() =>
                        registry.Register("sequential", new SequentialExecutionStrategy()));
                });

                builder.Add("GetStrategy_ShouldReturnRegisteredStrategy", () =>
                {
                    var registry = new StrategyRegistry();
                    var strategy = new SequentialExecutionStrategy();
                    registry.Register("sequential", strategy);

                    var retrieved = registry.GetStrategy("sequential");
                    Assert.AreSame(strategy, retrieved);
                });

                builder.Add("GetStrategy_NonExistent_ShouldThrow", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.ThrowsException<KeyNotFoundException>(() =>
                        registry.GetStrategy("nonexistent"));
                });

                builder.Add("Unregister_ShouldRemoveStrategy", () =>
                {
                    var registry = new StrategyRegistry();
                    registry.Register("sequential", new SequentialExecutionStrategy());

                    Assert.IsTrue(registry.Unregister("sequential"));
                    Assert.IsFalse(registry.HasStrategy("sequential"));
                });

                builder.Add("Unregister_NonExistent_ShouldReturnFalse", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.IsFalse(registry.Unregister("nonexistent"));
                });

                builder.Add("Clear_ShouldRemoveAllStrategies", () =>
                {
                    var registry = new StrategyRegistry();
                    registry.Register("sequential", new SequentialExecutionStrategy());
                    registry.Register("parallel", new ParallelExecutionStrategy());

                    registry.Clear();
                    Assert.AreEqual(0, registry.Count);
                });

                builder.Add("Count_ShouldReflectStrategyCount", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.AreEqual(0, registry.Count);
                    registry.Register("sequential", new SequentialExecutionStrategy());
                    Assert.AreEqual(1, registry.Count);
                    registry.Register("parallel", new ParallelExecutionStrategy());
                    Assert.AreEqual(2, registry.Count);
                    registry.Clear();
                    Assert.AreEqual(0, registry.Count);
                });

                builder.Add("GetDefault_ShouldReturnSequentialStrategy", () =>
                {
                    var strategy = StrategyRegistry.GetDefault();
                    Assert.IsInstanceOfType(strategy, typeof(SequentialExecutionStrategy));
                });

                builder.Add("Register_WithNullName_ShouldThrow", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        registry.Register(null!, new SequentialExecutionStrategy()));
                });

                builder.Add("Register_WithNullStrategy_ShouldThrow", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        registry.Register("test", null!));
                });

                builder.Add("GetAllStrategies_ShouldReturnAllNames", () =>
                {
                    var registry = new StrategyRegistry();
                    registry.Register("sequential", new SequentialExecutionStrategy());
                    registry.Register("parallel", new ParallelExecutionStrategy());

                    var names = registry.GetAllStrategies().ToList();
                    Assert.AreEqual(2, names.Count);
                    Assert.IsTrue(names.Contains("sequential"));
                    Assert.IsTrue(names.Contains("parallel"));
                });

                builder.Add("TryGetStrategy_Existing_ShouldReturnTrue", () =>
                {
                    var registry = new StrategyRegistry();
                    var strategy = new SequentialExecutionStrategy();
                    registry.Register("sequential", strategy);

                    Assert.IsTrue(registry.TryGetStrategy("sequential", out var retrieved));
                    Assert.AreSame(strategy, retrieved);
                });

                builder.Add("TryGetStrategy_NonExistent_ShouldReturnFalse", () =>
                {
                    var registry = new StrategyRegistry();

                    Assert.IsFalse(registry.TryGetStrategy("nonexistent", out _));
                });
            });
            result.AssertAllPassed();
        }
    }
}
