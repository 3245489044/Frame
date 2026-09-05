
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class DiagnosticScopeStoreTests
{

    [TestMethod]
    public void Register_And_GetById()
    {
        using var scope = new DiagnosticScope("TestScope");

        var retrieved = DiagnosticScopeStore.GetById(scope.CallChainId);

        Assert.IsNotNull(retrieved);
        Assert.AreEqual(scope.CallChainId, retrieved.CallChainId);
    }


    [TestMethod]
    public void Unregister_RemovesScope()
    {
        var scope = new DiagnosticScope("TestScope");
        var callChainId = scope.CallChainId;

        scope.Dispose();

        var retrieved = DiagnosticScopeStore.GetById(callChainId);
        Assert.IsNull(retrieved);
    }


    [TestMethod]
    public async Task GetCurrent_AsyncLocal_Flow()
    {
        using var scope = new DiagnosticScope("TestScope");

        var current = DiagnosticScopeStore.GetCurrent();
        Assert.IsNotNull(current);
        Assert.AreEqual(scope.CallChainId, current.CallChainId);

        await Task.Delay(10);
        current = DiagnosticScopeStore.GetCurrent();
        Assert.IsNotNull(current);
        Assert.AreEqual(scope.CallChainId, current.CallChainId);
    }


    [TestMethod]
    public async Task GetCurrent_ConfigureAwaitFalse_DoesNotLose()
    {
        using var scope = new DiagnosticScope("TestScope");

        await Task.Delay(10).ConfigureAwait(false);

        var current = DiagnosticScopeStore.GetCurrent();
        Assert.IsNotNull(current);
        Assert.AreEqual(scope.CallChainId, current.CallChainId);
    }


    [TestMethod]
    public void ActiveCount_ReturnsCorrectCount()
    {
        var scope1 = new DiagnosticScope("Scope1");
        var scope2 = new DiagnosticScope("Scope2");

        var allActive = DiagnosticScopeStore.GetAllActive();

        Assert.IsTrue(allActive.Count >= 2);

        scope1.Dispose();
        scope2.Dispose();
    }


    [TestMethod]
    public void Concurrent_Register_Unregister_ThreadSafe()
    {
        const int threadCount = 10;
        var scopes = new DiagnosticScope[threadCount];
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                scopes[i] = new DiagnosticScope($"ConcurrentScope-{i}");
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.AreEqual(0, exceptions.Count);
        for (int i = 0; i < threadCount; i++)
        {
            Assert.IsNotNull(scopes[i]);
            var retrieved = DiagnosticScopeStore.GetById(scopes[i].CallChainId);
            Assert.IsNotNull(retrieved);
        }

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                scopes[i]?.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.AreEqual(0, exceptions.Count);
        for (int i = 0; i < threadCount; i++)
        {
            var retrieved = DiagnosticScopeStore.GetById(scopes[i].CallChainId);
            Assert.IsNull(retrieved);
        }
    }
}
