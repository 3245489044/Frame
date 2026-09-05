using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Core
{
    [TestClass]
    public class ReferenceTrackingMiddlewareTests : TestBase
    {
        [TestMethod]
        public void RunAll_ReferenceTrackingMiddlewareTests()
        {
            var result = RunTestGroup("ReferenceTrackingMiddleware", builder =>
            {
                builder.Add("Track_ShouldTrackReference", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    var obj = new object();

                    middleware.Track("ref1", obj);

                    Assert.IsTrue(middleware.HasReference("ref1"));
                });

                builder.Add("Track_DuplicateKey_ShouldThrow", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    middleware.Track("ref1", new object());

                    Assert.ThrowsException<ArgumentException>(() =>
                        middleware.Track("ref1", new object()));
                });

                builder.Add("GetReference_ShouldReturnTrackedObject", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    var obj = new object();
                    middleware.Track("ref1", obj);

                    var retrieved = middleware.GetReference("ref1");
                    Assert.AreSame(obj, retrieved);
                });

                builder.Add("GetReference_NonExistent_ShouldReturnNull", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();

                    Assert.IsNull(middleware.GetReference("nonexistent"));
                });

                builder.Add("Release_ShouldRemoveReference", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    middleware.Track("ref1", new object());

                    Assert.IsTrue(middleware.Release("ref1"));
                    Assert.IsFalse(middleware.HasReference("ref1"));
                });

                builder.Add("Release_NonExistent_ShouldReturnFalse", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();

                    Assert.IsFalse(middleware.Release("nonexistent"));
                });

                builder.Add("Clear_ShouldRemoveAllReferences", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    middleware.Track("ref1", new object());
                    middleware.Track("ref2", new object());

                    middleware.Clear();
                    Assert.AreEqual(0, middleware.ReferenceCount);
                });

                builder.Add("ReferenceCount_ShouldReflectCount", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();

                    Assert.AreEqual(0, middleware.ReferenceCount);
                    middleware.Track("ref1", new object());
                    Assert.AreEqual(1, middleware.ReferenceCount);
                    middleware.Track("ref2", new object());
                    Assert.AreEqual(2, middleware.ReferenceCount);
                    middleware.Release("ref1");
                    Assert.AreEqual(1, middleware.ReferenceCount);
                });

                builder.Add("GetAllReferences_ShouldReturnAllKeys", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    middleware.Track("ref1", new object());
                    middleware.Track("ref2", new object());

                    var keys = middleware.GetAllReferences();
                    Assert.AreEqual(2, keys.Count());
                    Assert.IsTrue(keys.Contains("ref1"));
                    Assert.IsTrue(keys.Contains("ref2"));
                });

                builder.Add("Track_WithNullKey_ShouldThrow", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        middleware.Track(null!, new object()));
                });

                builder.Add("Track_WithNullObject_ShouldThrow", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        middleware.Track("ref1", null!));
                });

                builder.Add("Process_ShouldPassThroughInput", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    var errors = new ErrorCollector();
                    var ctx = new PipelineContext();

                    var result = middleware.Process("hello", errors, ctx);
                    Assert.AreEqual("hello", result);
                });

                builder.Add("Process_WithContext_ShouldStoreContext", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    var errors = new ErrorCollector();
                    var ctx = new PipelineContext();
                    ctx.SetItem("key", "value");

                    middleware.Process("hello", errors, ctx);

                    Assert.IsNotNull(middleware.LastContext);
                    Assert.AreEqual("value", middleware.LastContext?.GetItem<string>("key"));
                });

                builder.Add("Process_ShouldTrackInvocationCount", () =>
                {
                    var middleware = new ReferenceTrackingMiddleware();
                    var errors = new ErrorCollector();
                    var ctx = new PipelineContext();

                    Assert.AreEqual(0, middleware.InvocationCount);
                    middleware.Process("hello", errors, ctx);
                    Assert.AreEqual(1, middleware.InvocationCount);
                    middleware.Process("world", errors, ctx);
                    Assert.AreEqual(2, middleware.InvocationCount);
                });
            });
            result.AssertAllPassed();
        }
    }
}
