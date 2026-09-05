using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.PlaceholderFramework.Core;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class PlaceholderContextExtensionsTests : TestBase
    {
        [TestMethod]
        public void RunAll_PlaceholderContextExtensions()
        {
            var result = RunTestGroup("PlaceholderContextExtensions", builder =>
            {
                builder.Add("GetBatchId_ShouldGenerateNewId", () =>
                {
                    using var ctx = new SkeletonContext();
                    var batchId = ctx.GetBatchId();
                    return !string.IsNullOrEmpty(batchId) && batchId.Length == 32;
                });

                builder.Add("GetBatchId_ShouldReturnSameId", () =>
                {
                    using var ctx = new SkeletonContext();
                    var id1 = ctx.GetBatchId();
                    var id2 = ctx.GetBatchId();
                    return id1 == id2;
                });

                builder.Add("SetBatchId_ShouldOverride", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("test-batch-001");
                    return ctx.GetBatchId() == "test-batch-001";
                });

                builder.Add("GetModuleName_DefaultShouldBeUnknown", () =>
                {
                    using var ctx = new SkeletonContext();
                    return ctx.GetModuleName() == "Unknown";
                });

                builder.Add("SetModuleName_ShouldWork", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("TestModule");
                    return ctx.GetModuleName() == "TestModule";
                });

                builder.Add("SetPlaceholders_ShouldAddMultiple", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholders(new Dictionary<string, Placeholder>
                    {
                        ["key1"] = Placeholder.Input("input1"),
                        ["key2"] = Placeholder.Prev()
                    });
                    return ctx.GetPlaceholderCount() == 2;
                });

                builder.Add("GetAllPlaceholders_ShouldReturnAll", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholder("k1", Placeholder.Input("i1"));
                    ctx.SetPlaceholder("k2", Placeholder.Prev());
                    var all = ctx.GetAllPlaceholders().ToList();
                    return all.Count == 2
                        && all.Any(kv => kv.Key == "k1")
                        && all.Any(kv => kv.Key == "k2");
                });

                builder.Add("TryGetPlaceholder_Existing_ShouldReturnTrue", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholder("myKey", Placeholder.Input("myInput"));
                    return ctx.TryGetPlaceholder("myKey", out var ph)
                        && ph != null
                        && ph.Kind == PlaceholderKind.Input;
                });

                builder.Add("TryGetPlaceholder_NonExisting_ShouldReturnFalse", () =>
                {
                    using var ctx = new SkeletonContext();
                    return !ctx.TryGetPlaceholder("nonExistent", out _);
                });

                builder.Add("RemovePlaceholder_ShouldRemove", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholder("toRemove", Placeholder.Input("x"));
                    var removed = ctx.RemovePlaceholder("toRemove");
                    return removed && ctx.GetPlaceholderCount() == 0;
                });

                builder.Add("ClearPlaceholders_ShouldClearAll", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholder("a", Placeholder.Input("1"));
                    ctx.SetPlaceholder("b", Placeholder.Prev());
                    ctx.ClearPlaceholders();
                    return ctx.GetPlaceholderCount() == 0;
                });

                builder.Add("SetInputPlaceholder_ShouldCreateInput", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetInputPlaceholder("result", "userInput");
                    Assert.IsTrue(ctx.TryGetPlaceholder("result", out var ph));
                    return ph.Kind == PlaceholderKind.Input && ph.Key == "userInput";
                });

                builder.Add("SetPrevPlaceholder_ByKey_ShouldCreatePrev", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPrevPlaceholder("prev", "previousKey");
                    Assert.IsTrue(ctx.TryGetPlaceholder("prev", out var ph));
                    return ph.Kind == PlaceholderKind.Prev && ph.Key == "previousKey";
                });

                builder.Add("SetPrevPlaceholder_ByIndex_ShouldCreatePrev", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPrevPlaceholder("prevIdx", null, 2);
                    Assert.IsTrue(ctx.TryGetPlaceholder("prevIdx", out var ph));
                    return ph.Kind == PlaceholderKind.Prev && ph.Index == 2;
                });

                builder.Add("SetPrevPlaceholder_Default_ShouldCreatePrev", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPrevPlaceholder("prevDefault");
                    Assert.IsTrue(ctx.TryGetPlaceholder("prevDefault", out var ph));
                    return ph.Kind == PlaceholderKind.Prev;
                });

                builder.Add("SetNodeRefPlaceholder_ByKey_ShouldCreateNodeRef", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetNodeRefPlaceholder("ref", "node1", "outputKey");
                    Assert.IsTrue(ctx.TryGetPlaceholder("ref", out var ph));
                    return ph.Kind == PlaceholderKind.NodeReference
                        && ph.NodeId == "node1"
                        && ph.Key == "outputKey";
                });

                builder.Add("SetNodeRefPlaceholder_ByIndex_ShouldCreateNodeRef", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetNodeRefPlaceholder("refIdx", "node2", null, 3);
                    Assert.IsTrue(ctx.TryGetPlaceholder("refIdx", out var ph));
                    return ph.Kind == PlaceholderKind.NodeReference
                        && ph.NodeId == "node2"
                        && ph.Index == 3;
                });

                builder.Add("SetNodeIndexPlaceholder_ShouldCreateNodeIndex", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetNodeIndexPlaceholder("nodeIdx", 5);
                    Assert.IsTrue(ctx.TryGetPlaceholder("nodeIdx", out var ph));
                    return ph.Kind == PlaceholderKind.NodeIndex && ph.NodeIndex == 5;
                });

                builder.Add("GetPlaceholderCount_Empty_ShouldReturnZero", () =>
                {
                    using var ctx = new SkeletonContext();
                    return ctx.GetPlaceholderCount() == 0;
                });

                builder.Add("RemovePlaceholder_NonExisting_ShouldReturnFalse", () =>
                {
                    using var ctx = new SkeletonContext();
                    return !ctx.RemovePlaceholder("nonExistent");
                });

                builder.Add("SetPlaceholder_Overwrite_ShouldUpdate", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetPlaceholder("key", Placeholder.Input("old"));
                    ctx.SetPlaceholder("key", Placeholder.Input("new"));
                    Assert.IsTrue(ctx.TryGetPlaceholder("key", out var ph));
                    return ph.Kind == PlaceholderKind.Input && ph.Key == "new";
                });

                builder.Add("GetAllPlaceholders_Empty_ShouldReturnEmpty", () =>
                {
                    using var ctx = new SkeletonContext();
                    return ctx.GetAllPlaceholders().Count() == 0;
                });

                builder.Add("SetBatchId_EmptyString_ShouldWork", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("");
                    return ctx.GetBatchId() == "";
                });

                builder.Add("SetModuleName_EmptyString_ShouldWork", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("");
                    return ctx.GetModuleName() == "";
                });
            });

            result.AssertAllPassed();
        }
    }
}
