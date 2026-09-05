
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class DiagnosticScopeTests
{

    [TestMethod]
    public void DiagnosticScope_Create_RegistersInStore()
    {
        using var scope = new DiagnosticScope("TestScope");

        var retrieved = DiagnosticScopeStore.GetById(scope.CallChainId);
        Assert.IsNotNull(retrieved);
        Assert.AreSame(scope, retrieved);
    }


    [TestMethod]
    public void DiagnosticScope_Dispose_UnregistersFromStore()
    {
        var scope = new DiagnosticScope("TestScope");
        var callChainId = scope.CallChainId;

        scope.Dispose();

        var retrieved = DiagnosticScopeStore.GetById(callChainId);
        Assert.IsNull(retrieved);
    }


    [TestMethod]
    public void DiagnosticScope_Record_StoresInScopeNode()
    {
        using var scope = new DiagnosticScope("TestScope");

        scope.Record("test.event", new { key = "value" });

        var snapshot = scope.Freeze();
        Assert.AreEqual(1, snapshot.Events.Count);
        Assert.AreEqual("test.event", snapshot.Events[0].EventName);
    }


    [TestMethod]
    public void DiagnosticScope_PushScope_CreatesChildNode()
    {
        using var scope = new DiagnosticScope("TestScope");

        using (scope.PushScope("SubScope"))
        {
            scope.Record("sub.event", null);
        }

        var snapshot = scope.Freeze();
        Assert.AreEqual(1, snapshot.Children.Count);
        Assert.AreEqual("SubScope", snapshot.Children[0].Name);
        Assert.AreEqual(1, snapshot.Children[0].Events.Count);
        Assert.AreEqual("sub.event", snapshot.Children[0].Events[0].EventName);
    }


    [TestMethod]
    public void DiagnosticScope_PushScope_Dispose_PopsScope()
    {
        using var scope = new DiagnosticScope("TestScope");

        var popper = scope.PushScope("SubScope");
        Assert.AreEqual("SubScope", scope.CurrentNode.Name);
        popper.Dispose();

        Assert.AreEqual("TestScope", scope.CurrentNode.Name);
    }


    [TestMethod]
    public void DiagnosticScope_Freeze_CreatesSnapshot()
    {
        using var scope = new DiagnosticScope("TestScope");
        scope.Record("event1", 1);
        scope.Record("event2", "data");

        var snapshot = scope.Freeze();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("TestScope", snapshot.Name);
        Assert.AreEqual(2, snapshot.Events.Count);
    }


    [TestMethod]
    public void DiagnosticScope_SetState_GetState_Roundtrip()
    {
        using var scope = new DiagnosticScope("TestScope");

        scope.SetState("key1", "value1");
        scope.SetState("key2", 42);

        Assert.AreEqual("value1", scope.CurrentNode.GetState<string>("key1"));
        Assert.AreEqual(42, scope.CurrentNode.GetState<int>("key2"));
    }


    [TestMethod]
    public void DiagnosticScope_ToJson_ReturnsValidJson()
    {
        using var scope = new DiagnosticScope("TestScope");
        scope.Record("test.event", new { x = 1 });

        var json = scope.ToJson();

        Assert.IsFalse(string.IsNullOrEmpty(json));
        Assert.IsTrue(json.Contains("TestScope"));
        Assert.IsTrue(json.Contains("test.event"));
    }


    [TestMethod]
    public void DiagnosticScope_ToSummary_ReturnsSummary()
    {
        using var scope = new DiagnosticScope("TestScope");
        scope.Record("event.a", null);
        scope.Record("event.b", null);

        var summary = scope.ToSummary();

        Assert.IsFalse(string.IsNullOrEmpty(summary));
        Assert.IsTrue(summary.Contains("TestScope"));
        Assert.IsTrue(summary.Contains("2"));
    }


    [TestMethod]
    public void DiagnosticScope_CrossThread_Access()
    {
        using var scope = new DiagnosticScope("TestScope");
        var callChainId = scope.CallChainId;

        var task = Task.Run(() =>
        {
            var retrieved = DiagnosticScopeStore.GetById(callChainId);
            Assert.IsNotNull(retrieved);
            retrieved.Record("cross.thread.event", null);
        });
        task.Wait();

        var snapshot = scope.Freeze();
        Assert.AreEqual(1, snapshot.Events.Count);
        Assert.AreEqual("cross.thread.event", snapshot.Events[0].EventName);
    }
}
