using System.Collections.Generic;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Persistence.Tests;

[TestClass]
public class PersistenceConfigBinderTests
{
    [TestMethod]
    public void BindSection_MergesProviderAndPath()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "LiteDB",
                ["Persistence:DatabasePath"] = "x.db",
                ["Persistence:TablePrefix"] = "ur_",
                ["Persistence:AutoCreateSchema"] = "true",
            })
            .Build();

        var options = new PersistenceOptions { Provider = PersistenceProvider.Sqlite, TablePrefix = "ur_" };
        PersistenceConfigBinder.BindSection(config.GetSection("Persistence"), options);

        Assert.AreEqual(PersistenceProvider.LiteDB, options.Provider);
        Assert.AreEqual("x.db", options.DatabasePath);
        Assert.AreEqual("ur_", options.TablePrefix);
        Assert.IsTrue(options.AutoCreateSchema);
    }

    [TestMethod]
    public void BindSection_PreservesDefaultsOnMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = new PersistenceOptions { Provider = PersistenceProvider.Sqlite, TablePrefix = "ur_", AutoCreateSchema = true };
        PersistenceConfigBinder.BindSection(config.GetSection("Persistence"), options);

        Assert.AreEqual(PersistenceProvider.Sqlite, options.Provider);
        Assert.AreEqual("ur_", options.TablePrefix);
        Assert.IsTrue(options.AutoCreateSchema);
    }
}
