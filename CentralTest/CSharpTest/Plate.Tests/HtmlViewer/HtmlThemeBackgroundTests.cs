using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;

namespace Plate.HtmlViewer.Tests;

[TestClass]
public class HtmlThemeBackgroundTests
{
    private const string Css =
        ":root{--bg:#ffffff;--fg:#24292f;}" +
        "@media (prefers-color-scheme: dark){:root{--bg:#0d1117;--fg:#e6edf3;}}";

    [TestMethod]
    public void LightMode_ResolvesLightBg()
    {
        Assert.AreEqual(Color.FromArgb(255, 0xff, 0xff, 0xff), HtmlThemeBackground.Resolve(Css, ThemeMode.Light));
    }

    [TestMethod]
    public void DarkMode_ResolvesDarkMediaBg()
    {
        Assert.AreEqual(Color.FromArgb(255, 0x0d, 0x11, 0x17), HtmlThemeBackground.Resolve(Css, ThemeMode.Dark));
    }

    [TestMethod]
    public void System_ResolvesDefaultBg()
    {
        Assert.AreEqual(Color.FromArgb(255, 0xff, 0xff, 0xff), HtmlThemeBackground.Resolve(Css, ThemeMode.System));
    }

    [TestMethod]
    public void NullOrNoBg_ReturnsNull()
    {
        Assert.IsNull(HtmlThemeBackground.Resolve(null, ThemeMode.Light));
        Assert.IsNull(HtmlThemeBackground.Resolve("body{color:red}", ThemeMode.Light));
    }
}
