
using CommandFramework.Abstractions;
using FileDriven.UI.WinForms.Abstractions;
using ForntEndDriven.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileDriven.UI.Testes.ControlServices;

[TestClass]
public class ControlRegistryAwareFinderTests
{

    private class MockInnerFinder : IControlFinder
    {
        private readonly Dictionary<string, object> _controls = new();

        public void Add(string name, object control)
            => _controls[name] = control;

        public object? FindByName(object root, string name)
            => _controls.GetValueOrDefault(name);

        public object? FindByName(string name)
            => _controls.GetValueOrDefault(name);

        public T? FindByType<T>(object root) where T : class
            => _controls.Values.OfType<T>().FirstOrDefault();

        public List<T> FindAllByType<T>(object root) where T : class
            => _controls.Values.OfType<T>().ToList();

        public object? Find(object root, Func<object, bool> predicate)
            => _controls.Values.FirstOrDefault(predicate);

        public List<object> FindAll(object root, Func<object, bool> predicate)
            => _controls.Values.Where(predicate).Cast<object>().ToList();

        public object? FindInForm(string formName, string controlName)
            => null;

        public object? FindForm(object control)
            => null;

        public bool IsDescendantOf(object control, object ancestor)
            => false;

        public int GetDepth(object control)
            => 0;
    }


    [TestMethod]
    public void FindByName_ControlInRegistry_ReturnsRegistryControl()
    {
        var registry = new ControlRegistry();
        var registryControl = new Button { Name = "registryBtn" };
        registry.Register("myControl", registryControl);

        var innerFinder = new MockInnerFinder();
        var innerControl = new Button { Name = "innerBtn" };
        innerFinder.Add("myControl", innerControl);

        var finder = new ControlRegistryAwareFinder(innerFinder, registry);

        var result = finder.FindByName("myControl");

        Assert.IsNotNull(result);
        Assert.AreSame(registryControl, result);
    }

    [TestMethod]
    public void FindByName_ControlNotInRegistry_FallsBackToInnerFinder()
    {
        var registry = new ControlRegistry();
        var innerFinder = new MockInnerFinder();
        var innerControl = new Button { Name = "innerBtn" };
        innerFinder.Add("innerControl", innerControl);

        var finder = new ControlRegistryAwareFinder(innerFinder, registry);

        var result = finder.FindByName("innerControl");

        Assert.IsNotNull(result);
        Assert.AreSame(innerControl, result);
    }

    [TestMethod]
    public void FindByName_ControlNotFound_ReturnsNull()
    {
        var registry = new ControlRegistry();
        var innerFinder = new MockInnerFinder();
        var finder = new ControlRegistryAwareFinder(innerFinder, registry);

        var result = finder.FindByName("nonexistent");

        Assert.IsNull(result);
    }


    [TestMethod]
    public void FindByName_NoRegistry_DelegatesToInnerFinder()
    {
        var innerFinder = new MockInnerFinder();
        var expected = new Button();
        innerFinder.Add("btn", expected);

        var finder = new ControlRegistryAwareFinder(innerFinder, null);

        var result = finder.FindByName("btn");

        Assert.AreSame(expected, result);
    }

    [TestMethod]
    public void FindByName_NoRegistryAndNoMatch_ReturnsNull()
    {
        var innerFinder = new MockInnerFinder();
        var finder = new ControlRegistryAwareFinder(innerFinder, null);

        var result = finder.FindByName("nonexistent");

        Assert.IsNull(result);
    }


    [TestMethod]
    public void FindAll_WithRegistryAndInner_AggregatesResults()
    {
        var registry = new ControlRegistry();
        var registryBtn = new Button { Name = "regBtn" };
        registry.Register("regBtn", registryBtn);

        var innerFinder = new MockInnerFinder();
        var innerBtn = new Button { Name = "innerBtn" };
        innerFinder.Add("innerBtn", innerBtn);

        var finder = new ControlRegistryAwareFinder(innerFinder, registry);

        var panel = new Panel();
        panel.Controls.Add(registryBtn);

        var results = finder.FindAll(panel, c => c is Button);

        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(results.Contains(registryBtn));
    }


    [TestMethod]
    public void FindByType_WithRegistry_ReturnsFirstMatch()
    {
        var registry = new ControlRegistry();
        var registryBtn = new Button { Name = "regBtn" };
        registry.Register("regBtn", registryBtn);

        var innerFinder = new MockInnerFinder();
        var innerBtn = new Button { Name = "innerBtn" };
        innerFinder.Add("innerBtn", innerBtn);

        var finder = new ControlRegistryAwareFinder(innerFinder, registry);

        var panel = new Panel();
        panel.Controls.Add(registryBtn);

        var result = finder.FindByType<Button>(panel);

        Assert.IsNotNull(result);
        Assert.AreSame(registryBtn, result);
    }
}
