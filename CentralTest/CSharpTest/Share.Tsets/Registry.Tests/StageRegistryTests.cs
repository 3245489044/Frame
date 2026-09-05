using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class StageRegistryTests : TestBase
{
    private enum TestStage
    {
        Init,
        Process,
        Finalize
    }

    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("StageRegistry_Basic")
            .Add("Register_ShouldAddToStage", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "init-step");
                var steps = registry.GetValues(TestStage.Init).ToList();
                return steps.Count == 1 && steps[0] == "init-step";
            })
            .Add("Register_Multiple_ShouldAppend", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Process, "step1");
                registry.Register(TestStage.Process, "step2");
                var steps = registry.GetValues(TestStage.Process).ToList();
                return steps.Count == 2 && steps[0] == "step1" && steps[1] == "step2";
            })
            .Add("GetValues_EmptyStage_ShouldReturnEmpty", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                var steps = registry.GetValues(TestStage.Finalize).ToList();
                return steps.Count == 0;
            })
            .Add("GetStages_ShouldReturnAllStages", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "i1");
                registry.Register(TestStage.Process, "p1");
                var stages = registry.GetStages().ToList();
                return stages.Count == 2;
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "i1");
                registry.Register(TestStage.Process, "p1");
                registry.Clear();
                return registry.GetStages().Count() == 0;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_InsertAndReplace()
    {
        var result = new TestGroupBuilder("StageRegistry_InsertReplace")
            .Add("InsertAt_ShouldInsertAtPosition", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Process, "step1");
                registry.Register(TestStage.Process, "step3");
                registry.InsertAt(TestStage.Process, 1, "step2");
                var steps = registry.GetValues(TestStage.Process).ToList();
                return steps.Count == 3
                    && steps[0] == "step1"
                    && steps[1] == "step2"
                    && steps[2] == "step3";
            })
            .Add("Replace_ShouldReplaceValue", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Process, "old");
                registry.Replace(TestStage.Process, 0, "new");
                var steps = registry.GetValues(TestStage.Process).ToList();
                return steps.Count == 1 && steps[0] == "new";
            })
            .Add("Remove_ShouldRemoveValue", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Process, "a");
                registry.Register(TestStage.Process, "b");
                registry.Register(TestStage.Process, "c");
                registry.Remove(TestStage.Process, 1);
                var steps = registry.GetValues(TestStage.Process).ToList();
                return steps.Count == 2 && steps[0] == "a" && steps[1] == "c";
            })
            .Add("ClearStage_ShouldRemoveStage", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "i1");
                registry.Register(TestStage.Process, "p1");
                registry.ClearStage(TestStage.Init);
                return registry.GetStages().Count() == 1;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_Count()
    {
        var result = new TestGroupBuilder("StageRegistry_Count")
            .Add("TotalCount_ShouldReturnTotalItems", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "i1");
                registry.Register(TestStage.Init, "i2");
                registry.Register(TestStage.Process, "p1");
                return registry.TotalCount == 3;
            })
            .Add("GetCount_ShouldReturnItemsPerStage", () =>
            {
                var registry = new StageRegistryBase<TestStage, string>();
                registry.Register(TestStage.Init, "i1");
                registry.Register(TestStage.Process, "p1");
                registry.Register(TestStage.Process, "p2");
                return registry.GetCount(TestStage.Init) == 1
                    && registry.GetCount(TestStage.Process) == 2;
            })
            .Run();

        result.AssertAllPassed();
    }
}
