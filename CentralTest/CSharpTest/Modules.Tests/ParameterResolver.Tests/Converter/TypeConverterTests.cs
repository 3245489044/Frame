
using ParameterResolver.Converter;

namespace ParameterResolver.Tests.Converter;

[TestClass]
public class TypeConverterTests
{
    [TestMethod]
    public void Convert_StringToString_ShouldReturnSame()
    {
        var (result, error) = TypeConverter.Convert("hello", typeof(string));

        Assert.IsNull(error);
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void Convert_StringToInt_ShouldParse()
    {
        var (result, error) = TypeConverter.Convert("42", typeof(int));

        Assert.IsNull(error);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void Convert_StringToInt_Invalid_ShouldReturnError()
    {
        var (result, error) = TypeConverter.Convert("notANumber", typeof(int));

        Assert.IsNotNull(error);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Convert_StringToBool_True_ShouldParse()
    {
        var (result, error) = TypeConverter.Convert("true", typeof(bool));

        Assert.IsNull(error);
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void Convert_StringToBool_False_ShouldParse()
    {
        var (result, error) = TypeConverter.Convert("false", typeof(bool));

        Assert.IsNull(error);
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_StringToDouble_ShouldParse()
    {
        var (result, error) = TypeConverter.Convert("3.14", typeof(double));

        Assert.IsNull(error);
        Assert.AreEqual(3.14, (double)result!, 0.001);
    }

    [TestMethod]
    public void Convert_IntToString_ShouldConvert()
    {
        var (result, error) = TypeConverter.Convert(42, typeof(string));

        Assert.IsNull(error);
        Assert.AreEqual("42", result);
    }

    [TestMethod]
    public void Convert_Null_ShouldReturnDefault()
    {
        var (result, error) = TypeConverter.Convert(null, typeof(string));

        Assert.IsNull(error);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Convert_StringToEnum_ShouldParse()
    {
        var (result, error) = TypeConverter.Convert("Value1", typeof(TestEnum));

        Assert.IsNull(error);
        Assert.AreEqual(TestEnum.Value1, result);
    }

    [TestMethod]
    public void Convert_StringToEnum_Invalid_ShouldReturnError()
    {
        var (result, error) = TypeConverter.Convert("NonExistent", typeof(TestEnum));

        Assert.IsNotNull(error);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void CanConvert_StringToInt_ShouldReturnTrue()
    {
        Assert.IsTrue(TypeConverter.CanConvert(typeof(string), typeof(int)));
    }

    [TestMethod]
    public void CanConvert_IntToString_ShouldReturnTrue()
    {
        Assert.IsTrue(TypeConverter.CanConvert(typeof(int), typeof(string)));
    }

    private enum TestEnum
    {
        Value1,
        Value2
    }
}
