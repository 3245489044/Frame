
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Tests.Diagnostics;

[TestClass]
public class SourceLocationTests
{
    [TestMethod]
    public void Constructor_ShouldSetProperties()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name");

        Assert.AreEqual("config.json", location.FilePath);
        Assert.AreEqual("$.controls[0].name", location.JsonPath);
        Assert.IsNull(location.LineNumber);
        Assert.IsNull(location.ColumnNumber);
        Assert.IsNull(location.ContextSnippet);
    }

    [TestMethod]
    public void Constructor_WithAllParameters_ShouldSetAllProperties()
    {
        var location = new SourceLocation(
            "config.json",
            "$.controls[0].name",
            10,
            5,
            "\"name\": \"btn1\"");

        Assert.AreEqual("config.json", location.FilePath);
        Assert.AreEqual("$.controls[0].name", location.JsonPath);
        Assert.AreEqual(10, location.LineNumber);
        Assert.AreEqual(5, location.ColumnNumber);
        Assert.AreEqual("\"name\": \"btn1\"", location.ContextSnippet);
    }

    [TestMethod]
    public void ToString_ShouldFormatCorrectly()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name", 10, 5);

        var result = location.ToString();

        Assert.AreEqual("config.json:10:5 - $.controls[0].name", result);
    }

    [TestMethod]
    public void ToString_WithoutLineColumn_ShouldFormatCorrectly()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name");

        var result = location.ToString();

        Assert.AreEqual("config.json - $.controls[0].name", result);
    }

    [TestMethod]
    public void ToString_WithLineOnly_ShouldFormatCorrectly()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name", 10);

        var result = location.ToString();

        Assert.AreEqual("config.json:10 - $.controls[0].name", result);
    }

    [TestMethod]
    public void ToShortString_ShouldFormatCorrectly()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name");

        var result = location.ToShortString();

        Assert.AreEqual("config.json - $.controls[0].name", result);
    }

    [TestMethod]
    public void ToDetailedString_WithContext_ShouldIncludeContext()
    {
        var location = new SourceLocation(
            "config.json",
            "$.controls[0].name",
            10,
            5,
            "\"name\": \"btn1\"");

        var result = location.ToDetailedString();

        Assert.IsTrue(result.Contains("config.json:10:5"));
        Assert.IsTrue(result.Contains("$.controls[0].name"));
        Assert.IsTrue(result.Contains("\"name\": \"btn1\""));
    }

    [TestMethod]
    public void ToDetailedString_WithoutContext_ShouldNotIncludeContextSection()
    {
        var location = new SourceLocation("config.json", "$.controls[0].name");

        var result = location.ToDetailedString();

        Assert.IsTrue(result.Contains("config.json"));
        Assert.IsFalse(result.Contains("上下文:"));
    }

    [TestMethod]
    public void WithExpression_ShouldCreateVariant()
    {
        var original = new SourceLocation("config.json", "$.controls[0].name");
        var modified = original with { LineNumber = 42 };

        Assert.IsNull(original.LineNumber);
        Assert.AreEqual(42, modified.LineNumber);
        Assert.AreEqual(original.FilePath, modified.FilePath);
    }
}
