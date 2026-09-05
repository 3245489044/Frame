using System;
using System.IO;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Local;
using Operation.DataStoreDriven.Resolvers;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Managers;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class PersistenceProviderFactoryTests : IntegrationTestBase
{
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = CreateTempDirectory();

    [TestMethod]
    public void Factory_ResolvesSqlite()
        => Assert.IsInstanceOfType(PersistenceProviderFactory.Create(Options(PersistenceProvider.Sqlite)), typeof(SqlitePersistenceStore));

    [TestMethod]
    public void Factory_ResolvesLocal()
        => Assert.IsInstanceOfType(
            PersistenceProviderFactory.Create(
                Options(PersistenceProvider.Local),
                placeholderCheckpoints: new InMemoryCheckpointStore(),
                placeholderSnapshots: new InMemorySnapshotStore()),
            typeof(LocalPersistenceStore));

    [TestMethod]
    public void Factory_ResolvesGraph()
        => Assert.IsInstanceOfType(
            PersistenceProviderFactory.Create(
                Options(PersistenceProvider.Graph),
                new DefaultUniversalReferenceManager(),
                new InMemoryCheckpointStore(),
                new InMemorySnapshotStore()),
            typeof(GraphPersistenceStore));

    [TestMethod]
    public void Factory_ThrowsOnUnknownProvider()
    {
        var options = Options((PersistenceProvider)99);
        var threw = false;
        try { PersistenceProviderFactory.Create(options); }
        catch (NotSupportedException) { threw = true; }
        Assert.IsTrue(threw);
    }

    private static PersistenceOptions Options(PersistenceProvider provider) => new()
    {
        Provider = provider,
        DatabasePath = Path.Combine(_dir, $"{provider}.db"),
        TablePrefix = "ur_",
        AutoCreateSchema = true,
    };
}
