using BasicMethod.Pipeline;
using CentralConsole.Testing;
using CentralConsole.Testing.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Registry
{
    [TestClass]
    public class PipelineStageRegistryTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineStageRegistryTests()
        {
            var result = RunTestGroup("PipelineStageRegistry", builder =>
            {
                builder.Add("Register_ShouldAddStage", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stage = new MockPipelineStage("stage1");
                    registry.Register(stage);

                    Assert.AreEqual(1, registry.Stages.Count);
                    Assert.AreSame(stage, registry.Stages[0]);
                });

                builder.Add("Register_WithMultipleStages_ShouldSortByOrder", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage2") { Order = 2 });
                    registry.Register(new MockPipelineStage("stage1") { Order = 1 });
                    registry.Register(new MockPipelineStage("stage3") { Order = 3 });

                    Assert.AreEqual("stage1", registry.Stages[0].Name);
                    Assert.AreEqual("stage2", registry.Stages[1].Name);
                    Assert.AreEqual("stage3", registry.Stages[2].Name);
                });

                builder.Add("Register_WithSameOrder_ShouldPreserveInsertionOrder", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1") { Order = 1 });
                    registry.Register(new MockPipelineStage("stage2") { Order = 1 });
                    registry.Register(new MockPipelineStage("stage3") { Order = 1 });

                    Assert.AreEqual("stage1", registry.Stages[0].Name);
                    Assert.AreEqual("stage2", registry.Stages[1].Name);
                    Assert.AreEqual("stage3", registry.Stages[2].Name);
                });

                builder.Add("RegisterRange_ShouldAddMultipleStages", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stages = new IPipelineStage[]
                    {
                        new MockPipelineStage("stage1"),
                        new MockPipelineStage("stage2")
                    };
                    registry.RegisterRange(stages);

                    Assert.AreEqual(2, registry.Stages.Count);
                });

                builder.Add("Unregister_ShouldRemoveStage", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stage = new MockPipelineStage("stage1");
                    registry.Register(stage);

                    Assert.IsTrue(registry.Unregister(stage));
                    Assert.AreEqual(0, registry.Stages.Count);
                });

                builder.Add("Unregister_NonExistentStage_ShouldReturnFalse", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stage = new MockPipelineStage("stage1");

                    Assert.IsFalse(registry.Unregister(stage));
                });

                builder.Add("UnregisterByName_ShouldRemoveStage", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1"));

                    Assert.IsTrue(registry.UnregisterByName("stage1"));
                    Assert.AreEqual(0, registry.Stages.Count);
                });

                builder.Add("UnregisterByName_NonExistent_ShouldReturnFalse", () =>
                {
                    var registry = new PipelineStageRegistry();

                    Assert.IsFalse(registry.UnregisterByName("nonexistent"));
                });

                builder.Add("Clear_ShouldRemoveAllStages", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1"));
                    registry.Register(new MockPipelineStage("stage2"));

                    registry.Clear();
                    Assert.AreEqual(0, registry.Stages.Count);
                });

                builder.Add("Contains_ShouldReturnCorrectly", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stage = new MockPipelineStage("stage1");
                    registry.Register(stage);

                    Assert.IsTrue(registry.Contains(stage));
                    Assert.IsFalse(registry.Contains(new MockPipelineStage("nonexistent")));
                });

                builder.Add("ContainsName_ShouldReturnCorrectly", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1"));

                    Assert.IsTrue(registry.ContainsName("stage1"));
                    Assert.IsFalse(registry.ContainsName("nonexistent"));
                });

                builder.Add("FindByName_ShouldReturnStage", () =>
                {
                    var registry = new PipelineStageRegistry();
                    var stage = new MockPipelineStage("stage1");
                    registry.Register(stage);

                    var found = registry.FindByName("stage1");
                    Assert.IsNotNull(found);
                    Assert.AreSame(stage, found);
                });

                builder.Add("FindByName_NonExistent_ShouldReturnNull", () =>
                {
                    var registry = new PipelineStageRegistry();

                    Assert.IsNull(registry.FindByName("nonexistent"));
                });

                builder.Add("Count_ShouldReflectStageCount", () =>
                {
                    var registry = new PipelineStageRegistry();

                    Assert.AreEqual(0, registry.Count);
                    registry.Register(new MockPipelineStage("stage1"));
                    Assert.AreEqual(1, registry.Count);
                    registry.Register(new MockPipelineStage("stage2"));
                    Assert.AreEqual(2, registry.Count);
                    registry.Clear();
                    Assert.AreEqual(0, registry.Count);
                });

                builder.Add("Stages_ShouldBeReadOnly", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1"));

                    Assert.IsInstanceOfType(registry.Stages, typeof(IReadOnlyList<IPipelineStage>));
                });

                builder.Add("GetEnumerator_ShouldEnumerateStages", () =>
                {
                    var registry = new PipelineStageRegistry();
                    registry.Register(new MockPipelineStage("stage1"));
                    registry.Register(new MockPipelineStage("stage2"));

                    var names = new List<string>();
                    foreach (var stage in registry)
                    {
                        names.Add(stage.Name);
                    }

                    CollectionAssert.AreEqual(new[] { "stage1", "stage2" }, names);
                });
            });
            result.AssertAllPassed();
        }
    }
}
