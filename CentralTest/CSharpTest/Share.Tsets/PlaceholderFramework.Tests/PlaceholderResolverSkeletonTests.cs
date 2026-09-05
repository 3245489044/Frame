using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;
using Common.PlaceholderFramework.Storage;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class PlaceholderResolverSkeletonTests : TestBase
    {
        private class TestHandler : IPlaceholderHandler
        {
            public string Name => "Test";
            public List<(Placeholder ph, string value)> Handled { get; } = new();

            public bool CanHandle(Placeholder placeholder) => true;

            public void Handle(Placeholder placeholder, string resolvedValue,
                               SkeletonContext context, string batchId)
            {
                Handled.Add((placeholder, resolvedValue));
            }
        }

        private class InputOnlyHandler : IPlaceholderHandler
        {
            public string Name => "InputOnly";
            public List<string> HandledKeys { get; } = new();

            public bool CanHandle(Placeholder placeholder)
                => placeholder.Kind == PlaceholderKind.Input;

            public void Handle(Placeholder placeholder, string resolvedValue,
                               SkeletonContext context, string batchId)
            {
                HandledKeys.Add(placeholder.Key);
            }
        }

        [TestMethod]
        public void RunAll_PlaceholderResolverSkeleton()
        {
            var result = RunTestGroup("PlaceholderResolverSkeleton", builder =>
            {
                builder.Add("Execute_WithNoPlaceholders_ShouldReturnInput", () =>
                {
                    using var ctx = new SkeletonContext();
                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    var output = skeleton.Execute("test-input", ctx);

                    return output as string == "test-input"
                        && handler.Handled.Count == 0;
                });

                builder.Add("Execute_WithInputPlaceholder_ShouldResolveAndHandle", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("test-batch");
                    ctx.SetModuleName("TestModule");
                    ctx.SetPlaceholder("myInput", Placeholder.Input("userName"));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    skeleton.Execute("test-input", ctx);

                    return handler.Handled.Count == 1
                        && handler.Handled[0].ph.Key == "userName"
                        && handler.Handled[0].value == "userName";
                });

                builder.Add("Execute_WithMultipleHandlers_ShouldCallAll", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("test-batch");
                    ctx.SetPlaceholder("input1", Placeholder.Input("key1"));

                    var h1 = new TestHandler();
                    var h2 = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { h1, h2 }, store);

                    skeleton.Execute("input", ctx);

                    return h1.Handled.Count == 1 && h2.Handled.Count == 1;
                });

                builder.Add("Execute_WithInputOnlyHandler_ShouldOnlyHandleInput", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("test-batch");
                    ctx.SetPlaceholder("input1", Placeholder.Input("k1"));
                    ctx.SetPlaceholder("prev1", Placeholder.Prev());

                    var handler = new InputOnlyHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    skeleton.Execute("input", ctx);

                    return handler.HandledKeys.Count == 1
                        && handler.HandledKeys[0] == "k1";
                });

                builder.Add("Execute_ShouldSaveMetadataToStore", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("meta-batch");
                    ctx.SetModuleName("MetaModule");
                    ctx.SetPlaceholder("ph1", Placeholder.Input("testKey"));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    skeleton.Execute("input", ctx);

                    var saved = store.FindByBatch("meta-batch").ToList();
                    return saved.Count == 1
                        && saved[0].Key == "testKey"
                        && saved[0].SourceModule == "MetaModule"
                        && saved[0].HandlerName == "Test";
                });

                builder.Add("Execute_WithBatchSave_ShouldSaveAllAtOnce", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("batch-save");
                    ctx.SetModuleName("BatchModule");
                    ctx.SetPlaceholder("a", Placeholder.Input("k1"));
                    ctx.SetPlaceholder("b", Placeholder.Input("k2"));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store, batchSave: true);

                    skeleton.Execute("input", ctx);

                    var saved = store.FindByBatch("batch-save").ToList();
                    return saved.Count == 2;
                });

                builder.Add("Execute_HandlerException_ShouldNotAffectOtherHandlers", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("err-batch");
                    ctx.SetPlaceholder("ph1", Placeholder.Input("k1"));

                    var goodHandler = new TestHandler();
                    var badHandler = new ThrowingHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new IPlaceholderHandler[] { goodHandler, badHandler }, store);

                    skeleton.Execute("input", ctx);

                    return goodHandler.Handled.Count == 1;
                });

                builder.Add("Execute_WithPreviousOutputs_ShouldBuildContext", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("prev-batch");
                    ctx.SetData(new List<string> { "output1", "output2" },
                                "__Placeholder_PreviousOutputs");
                    ctx.SetPlaceholder("prev", Placeholder.PrevByIndex(0));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    skeleton.Execute("input", ctx);

                    return handler.Handled.Count == 1
                        && handler.Handled[0].value == "output1";
                });

                builder.Add("Execute_WithNoHandlers_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("no-handler");
                    ctx.SetPlaceholder("ph1", Placeholder.Input("k1"));

                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        Array.Empty<IPlaceholderHandler>(), store);

                    var output = skeleton.Execute("input", ctx);
                    return output as string == "input";
                });

                builder.Add("Execute_WithNullStore_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("null-store");
                    ctx.SetPlaceholder("ph1", Placeholder.Input("k1"));

                    var handler = new TestHandler();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, null);

                    var output = skeleton.Execute("input", ctx);
                    return output as string == "input";
                });

                builder.Add("Execute_WithPrevByIndex_OutOfRange_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("out-of-range");
                    ctx.SetData(new List<string> { "output1" },
                                "__Placeholder_PreviousOutputs");
                    ctx.SetPlaceholder("prev", Placeholder.PrevByIndex(99));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    var output = skeleton.Execute("input", ctx);
                    return output as string == "input";
                });

                builder.Add("Execute_WithMultiplePlaceholders_ShouldHandleAll", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetBatchId("multi");
                    ctx.SetModuleName("MultiModule");
                    ctx.SetPlaceholder("a", Placeholder.Input("k1"));
                    ctx.SetPlaceholder("b", Placeholder.Input("k2"));
                    ctx.SetPlaceholder("c", Placeholder.Input("k3"));

                    var handler = new TestHandler();
                    var store = new MemoryMetadataStore();
                    var skeleton = new PlaceholderResolverSkeleton(
                        new[] { handler }, store);

                    skeleton.Execute("input", ctx);

                    return handler.Handled.Count == 3;
                });
            });

            result.AssertAllPassed();
        }

        private class ThrowingHandler : IPlaceholderHandler
        {
            public string Name => "Throwing";

            public bool CanHandle(Placeholder placeholder) => true;

            public void Handle(Placeholder placeholder, string resolvedValue,
                               SkeletonContext context, string batchId)
            {
                throw new InvalidOperationException("Test exception");
            }
        }
    }
}
