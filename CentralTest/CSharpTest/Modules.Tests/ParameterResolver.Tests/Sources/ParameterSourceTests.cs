
using ParameterResolver.Sources;

namespace ParameterResolver.Tests.Sources;

[TestClass]
public class ParameterSourceTests
{
    [TestMethod]
    public void ReadValue_ExistingKey_ShouldReturnValue()
    {
        var store = new Dictionary<string, string?>
        {
            ["ctrl1.text"] = "Hello",
            ["ctrl1.value"] = "42"
        };
        var source = new VirtualStoreSource(store);

        var result = source.ReadValue("ctrl1.text");

        Assert.IsNotNull(result);
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void ReadValue_NonExistingKey_ShouldReturnNull()
    {
        var store = new Dictionary<string, string?>();
        var source = new VirtualStoreSource(store);

        var result = source.ReadValue("nonExistent");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReadValue_NullValue_ShouldReturnNull()
    {
        var store = new Dictionary<string, string?>
        {
            ["ctrl1.text"] = null
        };
        var source = new VirtualStoreSource(store);

        var result = source.ReadValue("ctrl1.text");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReadValue_WithControlName_ShouldReturnValue()
    {
        var store = new Dictionary<string, string?>
        {
            ["btn1"] = "clicked"
        };
        var source = new VirtualStoreSource(store);

        var result = source.ReadValue("btn1");

        Assert.AreEqual("clicked", result);
    }

    [TestMethod]
    public void ReadValue_EmptyStore_ShouldReturnNull()
    {
        var store = new Dictionary<string, string?>();
        var source = new VirtualStoreSource(store);

        Assert.IsNull(source.ReadValue("anyKey"));
    }

    [TestMethod]
    public void Constructor_NullStore_ShouldThrow()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new VirtualStoreSource(null!));
    }

    [TestMethod]
    public void ReadValue_MultipleKeys_ShouldReturnCorrectValues()
    {
        var store = new Dictionary<string, string?>
        {
            ["ctrl1.text"] = "Hello",
            ["ctrl1.value"] = "42",
            ["ctrl2.text"] = "World"
        };
        var source = new VirtualStoreSource(store);

        Assert.AreEqual("Hello", source.ReadValue("ctrl1.text"));
        Assert.AreEqual("42", source.ReadValue("ctrl1.value"));
        Assert.AreEqual("World", source.ReadValue("ctrl2.text"));
        Assert.IsNull(source.ReadValue("nonExistent"));
    }
}
