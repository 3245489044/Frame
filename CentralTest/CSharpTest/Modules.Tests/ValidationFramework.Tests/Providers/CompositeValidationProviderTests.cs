
using ValidationFramework.Providers;

namespace ValidationFramework.Tests.Providers;

[TestClass]
public class CompositeValidationProviderTests
{
    [TestMethod]
    public void 构造_应设置名称()
    {
        var provider = new CompositeValidationProvider("P");

        Assert.AreEqual("P", provider.Name);
    }

    [TestMethod]
    public void HasItem_函数注册_应返回true()
    {
        var provider = new CompositeValidationProvider(
            "P",
            functions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["F"] = Array.Empty<string>()
            });

        Assert.IsTrue(provider.HasItem("F"));
        Assert.IsTrue(provider.HasFunction("F"));
    }

    [TestMethod]
    public void HasItem_对象注册_应返回true()
    {
        var provider = new CompositeValidationProvider(
            "P",
            objects: new[] { "Obj" });

        Assert.IsTrue(provider.HasItem("Obj"));
        Assert.IsFalse(provider.HasFunction("Obj"));
    }

    [TestMethod]
    public void HasItem_未注册_应返回false()
    {
        var provider = new CompositeValidationProvider("P");

        Assert.IsFalse(provider.HasItem("Nope"));
        Assert.IsFalse(provider.HasFunction("Nope"));
    }

    [TestMethod]
    public void GetItems_应合并函数与对象并去重()
    {
        var provider = new CompositeValidationProvider(
            "P",
            functions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["Shared"] = Array.Empty<string>(),
                ["F"] = Array.Empty<string>()
            },
            objects: new[] { "Shared", "Obj" });

        var items = provider.GetItems();

        Assert.AreEqual(3, items.Count);
        CollectionAssert.Contains(items.ToList(), "Shared");
        CollectionAssert.Contains(items.ToList(), "F");
        CollectionAssert.Contains(items.ToList(), "Obj");
    }

    [TestMethod]
    public void GetItemType_函数_应返回签名信息()
    {
        var provider = new CompositeValidationProvider(
            "P",
            functions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["F"] = new[] { "string", "int" }
            });

        Assert.AreEqual("function[2]", provider.GetItemType("F"));
    }

    [TestMethod]
    public void GetItemType_对象_应返回object()
    {
        var provider = new CompositeValidationProvider("P", objects: new[] { "Obj" });

        Assert.AreEqual("object", provider.GetItemType("Obj"));
    }

    [TestMethod]
    public void GetItemType_未注册_应返回null()
    {
        var provider = new CompositeValidationProvider("P");

        Assert.IsNull(provider.GetItemType("Nope"));
    }

    [TestMethod]
    public void GetParameterTypes_已注册函数_应返回参数列表()
    {
        var provider = new CompositeValidationProvider(
            "P",
            functions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["F"] = new[] { "string" }
            });

        var types = provider.GetParameterTypes("F");

        Assert.AreEqual(1, types!.Count);
        Assert.AreEqual("string", types[0]);
    }

    [TestMethod]
    public void GetParameterTypes_未知函数_应返回null()
    {
        var provider = new CompositeValidationProvider("P");

        Assert.IsNull(provider.GetParameterTypes("Nope"));
    }

    [TestMethod]
    public void GetFunctions_应返回全部函数名()
    {
        var provider = new CompositeValidationProvider(
            "P",
            functions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["F1"] = Array.Empty<string>(),
                ["F2"] = Array.Empty<string>()
            });

        var functions = provider.GetFunctions();

        Assert.AreEqual(2, functions.Count);
    }
}