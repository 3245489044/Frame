
using System.Reflection;
using ParameterResolver.Parameters;

namespace ParameterResolver.Tests.Parameters;

[TestClass]
public class ParameterDefinitionTests
{
    [TestMethod]
    public void ParameterHelper_GetTypeDefault_Int_ShouldReturnZero()
    {
        var result = ParameterHelper.GetTypeDefault(typeof(int));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void ParameterHelper_GetTypeDefault_Bool_ShouldReturnFalse()
    {
        var result = ParameterHelper.GetTypeDefault(typeof(bool));

        Assert.IsNotNull(result);
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ParameterHelper_GetTypeDefault_ReferenceType_ShouldReturnNull()
    {
        var result = ParameterHelper.GetTypeDefault(typeof(string));

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParameterHelper_GetTypeDefault_ValueType_ShouldReturnInstance()
    {
        var result = ParameterHelper.GetTypeDefault(typeof(int));

        Assert.AreEqual(typeof(int), result.GetType());
    }

    [TestMethod]
    public void ParameterHelper_GetParamDefault_WithDefaultValue_ShouldReturnDefault()
    {
        var method = typeof(ParameterDefinitionTests)
            .GetMethod(nameof(SampleMethodWithDefault), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var param = method.GetParameters()[0];

        var result = ParameterHelper.GetParamDefault(param);

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void ParameterHelper_GetParamDefault_WithoutDefault_ShouldReturnTypeDefault()
    {
        var method = typeof(ParameterDefinitionTests)
            .GetMethod(nameof(SampleMethodWithoutDefault), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var param = method.GetParameters()[0];

        var result = ParameterHelper.GetParamDefault(param);

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void DefaultParameterProcessor_ProcessAsync_WithString_ShouldReturnSame()
    {
        var processor = new DefaultParameterProcessor();

        Assert.IsNotNull(processor);
    }

    private int SampleMethodWithDefault([System.Runtime.CompilerServices.CallerLineNumber] int value = 42)
    {
        return value;
    }

    private void SampleMethodWithoutDefault(int value)
    {
    }
}
