
using ParameterResolver.Formatting;

namespace ParameterResolver.Tests.Formatting;

[TestClass]
public class DiagnosticFormatterTests
{
    [TestMethod]
    public void Format_Null_ShouldReturnEmpty()
    {
        var result = ValueFormatter.Format(null);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Format_String_ShouldReturnSame()
    {
        var result = ValueFormatter.Format("hello");

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void Format_Int_ShouldReturnString()
    {
        var result = ValueFormatter.Format(42);

        Assert.AreEqual("42", result);
    }

    [TestMethod]
    public void Format_Bool_True_ShouldReturnTrue()
    {
        var result = ValueFormatter.Format(true);

        Assert.AreEqual("True", result);
    }

    [TestMethod]
    public void Format_Bool_False_ShouldReturnFalse()
    {
        var result = ValueFormatter.Format(false);

        Assert.AreEqual("False", result);
    }

    [TestMethod]
    public void Format_DateTime_ShouldReturnFormatted()
    {
        var dt = new DateTime(2026, 7, 18, 10, 30, 0);
        var result = ValueFormatter.Format(dt);

        Assert.AreEqual("2026-07-18 10:30:00", result);
    }

    [TestMethod]
    public void Format_WithFormatString_ShouldApplyFormat()
    {
        var dt = new DateTime(2026, 7, 18);
        var result = ValueFormatter.Format(dt, "yyyy/MM/dd");

        Assert.AreEqual("2026/07/18", result);
    }

    [TestMethod]
    public void Format_WithNullFormat_ShouldUseDefault()
    {
        var result = ValueFormatter.Format("hello", null);

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void Format_Object_ShouldSerialize()
    {
        var obj = new { Name = "test", Value = 42 };
        var result = ValueFormatter.Format(obj);

        Assert.IsTrue(result.Contains("test"));
        Assert.IsTrue(result.Contains("42"));
    }
}
