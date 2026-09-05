
using ValidationFramework.Abstractions;

namespace ValidationFramework.Tests.Abstractions;

[TestClass]
public class ValidationContextTests
{
    [TestMethod]
    public void 默认构造_应使用宽松模式()
    {
        IValidationContext ctx = new ValidationContext();

        Assert.AreEqual(ValidationMode.Loose, ctx.Mode);
        Assert.IsFalse(ctx.IsSilent);
        Assert.IsFalse(ctx.IsStrict);
        Assert.IsNull(ctx.FilePath);
        Assert.IsNull(ctx.RawJson);
    }

    [TestMethod]
    public void 构造_应设置模式与定位()
    {
        IValidationContext ctx = new ValidationContext(ValidationMode.Strict, "config.json", "{}");

        Assert.AreEqual(ValidationMode.Strict, ctx.Mode);
        Assert.IsTrue(ctx.IsStrict);
        Assert.IsFalse(ctx.IsSilent);
        Assert.AreEqual("config.json", ctx.FilePath);
        Assert.AreEqual("{}", ctx.RawJson);
    }

    [TestMethod]
    public void 静默模式_应返回IsSilent()
    {
        IValidationContext ctx = new ValidationContext(ValidationMode.Silent);

        Assert.IsTrue(ctx.IsSilent);
        Assert.IsFalse(ctx.IsStrict);
    }

    [TestMethod]
    public void WithProvider_应可获取同类型提供者()
    {
        var ctx = new ValidationContext();
        var provider = new TestProvider();

        ctx.WithProvider<TestProvider>(provider);

        Assert.AreSame(provider, ctx.GetProvider<TestProvider>());
    }

    [TestMethod]
    public void GetProvider_未注册类型_应返回null()
    {
        var ctx = new ValidationContext();

        Assert.IsNull(ctx.GetProvider<TestProvider>());
    }

    [TestMethod]
    public void WithProvider_按接口注册_可按接口类型获取()
    {
        var ctx = new ValidationContext();
        var provider = new TestProvider();

        ctx.WithProvider<ITestProvider>(provider);

        Assert.AreSame(provider, ctx.GetProvider<ITestProvider>());
    }

    [TestMethod]
    public void WithProvider_null_应抛异常()
    {
        var ctx = new ValidationContext();

        Assert.ThrowsException<ArgumentNullException>(
            () => ctx.WithProvider<TestProvider>(null!));
    }

    [TestMethod]
    public void WithProvider_同类型重复注册_应覆盖()
    {
        var ctx = new ValidationContext();
        var first = new TestProvider { Value = 1 };
        var second = new TestProvider { Value = 2 };

        ctx.WithProvider<TestProvider>(first);
        ctx.WithProvider<TestProvider>(second);

        Assert.AreSame(second, ctx.GetProvider<TestProvider>());
    }

    [TestMethod]
    public void WithProviders_批量注册_应按具体类型存储()
    {
        var ctx = new ValidationContext();
        var provider = new TestProvider();

        ctx.WithProviders(new object[] { provider });

        Assert.AreSame(provider, ctx.GetProvider<TestProvider>());
    }

    [TestMethod]
    public void WithProviders_含null元素_应跳过()
    {
        var ctx = new ValidationContext();
        var provider = new TestProvider();

        ctx.WithProviders(new object?[] { null, provider }!);

        Assert.AreSame(provider, ctx.GetProvider<TestProvider>());
    }

    [TestMethod]
    public void GetProvider_按Type重载_应返回实例()
    {
        var ctx = new ValidationContext();
        var provider = new TestProvider();
        ctx.WithProvider<TestProvider>(provider);

        Assert.AreSame(provider, ctx.GetProvider(typeof(TestProvider)));
    }

    [TestMethod]
    public void GetProvider_按Type重载_未注册_应返回null()
    {
        var ctx = new ValidationContext();

        Assert.IsNull(ctx.GetProvider(typeof(TestProvider)));
    }

    [TestMethod]
    public void FilePath_和RawJson_应可set()
    {
        var ctx = new ValidationContext();

        ctx.FilePath = "a.json";
        ctx.RawJson = "{}";

        Assert.AreEqual("a.json", ctx.FilePath);
        Assert.AreEqual("{}", ctx.RawJson);
    }

    public class TestProvider : ITestProvider
    {
        public int Value { get; set; }
    }

    public interface ITestProvider { }
}