using BasicMethod.Pipeline;
using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class PipelineContextTests : TestBase
    {
        [TestMethod]
        public void RunAll_PipelineContextTests()
        {
            var result = RunTestGroup("PipelineContext", builder =>
            {
                builder.Add("Constructor_ShouldCreateValidContext", () =>
                {
                    var ctx = new PipelineContext();

                    Assert.IsNotNull(ctx.Id);
                    Assert.IsFalse(string.IsNullOrEmpty(ctx.Id));
                    Assert.IsFalse(ctx.IsCancelled);
                    Assert.IsNotNull(ctx.Items);
                    Assert.AreEqual(0, ctx.Items.Count);
                    Assert.IsNull(ctx.CancelReason);
                    Assert.IsNull(ctx.SourceFile);
                    Assert.IsNull(ctx.Services);
                });

                builder.Add("Constructor_WithCancellationToken_ShouldLinkToken", () =>
                {
                    using var cts = new CancellationTokenSource();
                    var ctx = new PipelineContext(cts.Token);

                    Assert.IsFalse(ctx.IsCancelled);
                    cts.Cancel();
                    Assert.IsTrue(ctx.IsCancelled);
                });

                builder.Add("Constructor_WithServices_ShouldStoreServiceProvider", () =>
                {
                    var services = new ServiceCollection();
                    var ctx = new PipelineContext(services: services);

                    Assert.AreSame(services, ctx.Services);
                });

                builder.Add("Cancel_ShouldSetCancelledAndReason", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.Cancel("test reason");

                    Assert.IsTrue(ctx.IsCancelled);
                    Assert.AreEqual("test reason", ctx.CancelReason);
                });

                builder.Add("Cancel_WithoutReason_ShouldSetCancelled", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.Cancel();

                    Assert.IsTrue(ctx.IsCancelled);
                    Assert.IsNull(ctx.CancelReason);
                });

                builder.Add("Cancel_OnDisposedContext_ShouldThrow", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.Dispose();

                    Assert.ThrowsException<ObjectDisposedException>(() => ctx.Cancel());
                });

                builder.Add("SetItem_And_GetItem_ShouldWork", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.SetItem("key1", "value1");
                    ctx.SetItem("key2", 42);

                    Assert.AreEqual("value1", ctx.GetItem<string>("key1"));
                    Assert.AreEqual(42, ctx.GetItem<int>("key2"));
                });

                builder.Add("Indexer_ShouldGetAndSetItems", () =>
                {
                    var ctx = new PipelineContext();
                    ctx["key1"] = "value1";

                    Assert.AreEqual("value1", ctx["key1"]);
                });

                builder.Add("Indexer_SettingNull_ShouldRemoveItem", () =>
                {
                    var ctx = new PipelineContext();
                    ctx["key1"] = "value1";
                    ctx["key1"] = null;

                    Assert.IsNull(ctx["key1"]);
                    Assert.IsFalse(ctx.HasItem("key1"));
                });

                builder.Add("HasItem_ShouldReturnCorrectly", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.SetItem("key1", "value1");

                    Assert.IsTrue(ctx.HasItem("key1"));
                    Assert.IsFalse(ctx.HasItem("nonexistent"));
                });

                builder.Add("TryGetValue_ShouldReturnCorrectly", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.SetItem("key1", "value1");

                    Assert.IsTrue(ctx.TryGetValue("key1", out var value));
                    Assert.AreEqual("value1", value);
                    Assert.IsFalse(ctx.TryGetValue("nonexistent", out _));
                });

                builder.Add("SetRange_ShouldAddMultipleItems", () =>
                {
                    var ctx = new PipelineContext();
                    var items = new Dictionary<string, object?>
                    {
                        ["key1"] = "value1",
                        ["key2"] = 42
                    };
                    ctx.SetRange(items);

                    Assert.AreEqual(2, ctx.Items.Count);
                    Assert.AreEqual("value1", ctx.GetItem<string>("key1"));
                    Assert.AreEqual(42, ctx.GetItem<int>("key2"));
                });

                builder.Add("Items_ShouldBeReadOnly", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.SetItem("key1", "value1");

                    Assert.IsInstanceOfType(ctx.Items, typeof(IReadOnlyDictionary<string, object?>));
                });

                builder.Add("CancellationToken_ShouldBeUsable", () =>
                {
                    var ctx = new PipelineContext();
                    var token = ctx.CancellationToken;

                    Assert.IsFalse(token.IsCancellationRequested);
                    ctx.Cancel();
                    Assert.IsTrue(token.IsCancellationRequested);
                });

                builder.Add("Dispose_ShouldCancelAndSuppressFinalize", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.Dispose();

                    Assert.IsTrue(ctx.IsCancelled);
                    ctx.Dispose();
                });

                builder.Add("Id_ShouldBeUnique", () =>
                {
                    var ctx1 = new PipelineContext();
                    var ctx2 = new PipelineContext();

                    Assert.AreNotEqual(ctx1.Id, ctx2.Id);
                });

                builder.Add("CreatedAt_ShouldBeSet", () =>
                {
                    var before = DateTime.UtcNow;
                    var ctx = new PipelineContext();
                    var after = DateTime.UtcNow;

                    Assert.IsTrue(ctx.CreatedAt >= before);
                    Assert.IsTrue(ctx.CreatedAt <= after);
                });

                builder.Add("SourceFile_ShouldBeSettable", () =>
                {
                    var ctx = new PipelineContext();
                    ctx.SourceFile = "test.txt";

                    Assert.AreEqual("test.txt", ctx.SourceFile);
                });
            });
            result.AssertAllPassed();
        }
    }

    internal class ServiceCollection : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
