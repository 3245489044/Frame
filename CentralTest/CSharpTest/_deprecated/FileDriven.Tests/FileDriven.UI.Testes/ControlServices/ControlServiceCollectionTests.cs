
using CommandFramework.Abstractions;
using FileDriven.UI.WinForms.Abstractions;
using ForntEndDriven.Implementation;
using ForntEndDriven.Registration;
using FrameworkAbstractions.ComponentRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileDriven.UI.Testes.ControlServices;

[TestClass]
public class ControlServiceCollectionTests
{
    [TestInitialize]
    public void Setup()
    {
        ControlServiceCollection.Reset();
        ComponentRegistry.Instance.Clear();
    }


    [TestMethod]
    public void RegisterAll_AfterRegistration_IControlFinderIsResolvable()
    {
        ControlServiceCollection.RegisterAll();

        var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
        Assert.IsNotNull(finder);
        Assert.IsInstanceOfType(finder, typeof(ControlRegistryAwareFinder));
    }

    [TestMethod]
    public void RegisterAll_AfterRegistration_IControlValueWriterIsResolvable()
    {
        ControlServiceCollection.RegisterAll();

        var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
        Assert.IsNotNull(writer);
        Assert.IsInstanceOfType(writer, typeof(CompositeValueWriter));
    }

    [TestMethod]
    public void RegisterAll_AfterRegistration_MetaCommandIControlServiceIsResolvable()
    {
        ControlServiceCollection.RegisterAll();

        var service = ComponentRegistry.Instance.Resolve<MetaCommand.IControlService>();
        Assert.IsNotNull(service);
        Assert.IsInstanceOfType(service, typeof(UnifiedControlService));
    }


    [TestMethod]
    public void RegisterAll_CalledTwice_SecondCallIsIdempotent()
    {
        ControlServiceCollection.RegisterAll();

        ControlServiceCollection.RegisterAll();

        var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
        Assert.IsNotNull(finder);
    }


    [TestMethod]
    public void RegisterAll_WithControlRegistry_RegistryIsRegistered()
    {
        var registry = new ControlRegistry();

        ControlServiceCollection.RegisterAll(registry);

        var resolved = ComponentRegistry.Instance.Resolve<IControlRegistry>();
        Assert.IsNotNull(resolved);
        Assert.AreSame(registry, resolved);
    }

    [TestMethod]
    public void RegisterAll_WithoutControlRegistry_RegistryIsNotRegistered()
    {
        ControlServiceCollection.RegisterAll();

        var resolved = ComponentRegistry.Instance.Resolve<IControlRegistry>();
        Assert.IsNull(resolved);
    }


    [TestMethod]
    public void Reset_ThenRegisterAll_ComponentsAreResolvableAgain()
    {
        ControlServiceCollection.RegisterAll();
        ControlServiceCollection.Reset();

        ControlServiceCollection.RegisterAll();

        var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
        Assert.IsNotNull(finder);
    }


    [TestMethod]
    public void RegisterAll_FirstWithoutThenWithRegistry_RegistryIsUpdated()
    {
        ControlServiceCollection.RegisterAll();
        var registry = new ControlRegistry();

        ControlServiceCollection.RegisterAll(registry);

        var resolved = ComponentRegistry.Instance.Resolve<IControlRegistry>();
        Assert.IsNotNull(resolved);
        Assert.AreSame(registry, resolved);
    }
}
