
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Tests.Diagnostics;

[TestClass]
public class SeverityTests
{

    [TestMethod]
    public void IsBlocking_Error_StrictMode_ShouldReturnTrue()
    {
        Assert.IsTrue(Severity.Error.IsBlocking(true));
    }

    [TestMethod]
    public void IsBlocking_Error_LenientMode_ShouldReturnFalse()
    {
        Assert.IsFalse(Severity.Error.IsBlocking(false));
    }

    [TestMethod]
    public void IsBlocking_Warning_StrictMode_ShouldReturnFalse()
    {
        Assert.IsFalse(Severity.Warning.IsBlocking(true));
    }

    [TestMethod]
    public void IsBlocking_Info_StrictMode_ShouldReturnFalse()
    {
        Assert.IsFalse(Severity.Info.IsBlocking(true));
    }


    [TestMethod]
    public void ShouldLog_Error_ShouldReturnTrue()
    {
        Assert.IsTrue(Severity.Error.ShouldLog());
    }

    [TestMethod]
    public void ShouldLog_Warning_ShouldReturnTrue()
    {
        Assert.IsTrue(Severity.Warning.ShouldLog());
    }

    [TestMethod]
    public void ShouldLog_Info_ShouldReturnFalse()
    {
        Assert.IsFalse(Severity.Info.ShouldLog());
    }


    [TestMethod]
    public void ToDisplayString_Error_ShouldReturnChinese()
    {
        Assert.AreEqual("错误", Severity.Error.ToDisplayString());
    }

    [TestMethod]
    public void ToDisplayString_Warning_ShouldReturnChinese()
    {
        Assert.AreEqual("警告", Severity.Warning.ToDisplayString());
    }

    [TestMethod]
    public void ToDisplayString_Info_ShouldReturnChinese()
    {
        Assert.AreEqual("信息", Severity.Info.ToDisplayString());
    }


    [TestMethod]
    public void SeverityOrder_ErrorGreaterThanWarning()
    {
        Assert.IsTrue(Severity.Error > Severity.Warning);
    }

    [TestMethod]
    public void SeverityOrder_WarningGreaterThanInfo()
    {
        Assert.IsTrue(Severity.Warning > Severity.Info);
    }

    [TestMethod]
    public void SeverityOrder_ErrorGreaterThanInfo()
    {
        Assert.IsTrue(Severity.Error > Severity.Info);
    }
}
