
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;

namespace WdUI.Tests
{
    [TestClass]
    public class WdUIUsageConversionTests : TestBase
    {
        [TestMethod]
        public void RunAll_ToDockStyle()
        {
            var result = new TestGroupBuilder("ToDockStyle")
                .Add("Top_ShouldReturnDockStyleTop", () =>
                    WdUIUsage.ToDockStyle("Top") == DockStyle.Top)
                .Add("Bottom_ShouldReturnDockStyleBottom", () =>
                    WdUIUsage.ToDockStyle("Bottom") == DockStyle.Bottom)
                .Add("Left_ShouldReturnDockStyleLeft", () =>
                    WdUIUsage.ToDockStyle("Left") == DockStyle.Left)
                .Add("Right_ShouldReturnDockStyleRight", () =>
                    WdUIUsage.ToDockStyle("Right") == DockStyle.Right)
                .Add("Fill_ShouldReturnDockStyleFill", () =>
                    WdUIUsage.ToDockStyle("Fill") == DockStyle.Fill)
                .Add("None_ShouldReturnDockStyleNone", () =>
                    WdUIUsage.ToDockStyle("None") == DockStyle.None)
                .Add("Invalid_ShouldReturnDockStyleNone", () =>
                    WdUIUsage.ToDockStyle("Invalid") == DockStyle.None)
                .Add("Empty_ShouldReturnDockStyleNone", () =>
                    WdUIUsage.ToDockStyle("") == DockStyle.None)
                .Add("CaseInsensitive_Top_ShouldWork", () =>
                    WdUIUsage.ToDockStyle("top") == DockStyle.Top)
                .Add("CaseInsensitive_FILL_ShouldWork", () =>
                    WdUIUsage.ToDockStyle("FILL") == DockStyle.Fill)
                .Add("MixedCase_ShouldWork", () =>
                    WdUIUsage.ToDockStyle("Left") == DockStyle.Left)
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ToAnchorStyle()
        {
            var result = new TestGroupBuilder("ToAnchorStyle")
                .Add("Top_ShouldReturnAnchorStylesTop", () =>
                    WdUIUsage.ToAnchorStyle("Top") == AnchorStyles.Top)
                .Add("Bottom_ShouldReturnAnchorStylesBottom", () =>
                    WdUIUsage.ToAnchorStyle("Bottom") == AnchorStyles.Bottom)
                .Add("Left_ShouldReturnAnchorStylesLeft", () =>
                    WdUIUsage.ToAnchorStyle("Left") == AnchorStyles.Left)
                .Add("Right_ShouldReturnAnchorStylesRight", () =>
                    WdUIUsage.ToAnchorStyle("Right") == AnchorStyles.Right)
                .Add("TopLeft_ShouldReturnCombined", () =>
                    WdUIUsage.ToAnchorStyle("Top,Left") == (AnchorStyles.Top | AnchorStyles.Left))
                .Add("AllFour_ShouldReturnCombined", () =>
                    WdUIUsage.ToAnchorStyle("Top,Bottom,Left,Right") ==
                    (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right))
                .Add("Invalid_ShouldReturnNone", () =>
                    WdUIUsage.ToAnchorStyle("Invalid") == AnchorStyles.None)
                .Add("Empty_ShouldReturnNone", () =>
                    WdUIUsage.ToAnchorStyle("") == AnchorStyles.None)
                .Add("CaseInsensitive_ShouldWork", () =>
                    WdUIUsage.ToAnchorStyle("top,left") == (AnchorStyles.Top | AnchorStyles.Left))
                .Add("WithSpaces_ShouldWork", () =>
                    WdUIUsage.ToAnchorStyle(" Top , Bottom ") == (AnchorStyles.Top | AnchorStyles.Bottom))
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ToWindowState()
        {
            var result = new TestGroupBuilder("ToWindowState")
                .Add("Normal_ShouldReturnFormWindowStateNormal", () =>
                    WdUIUsage.ToWindowState("Normal") == FormWindowState.Normal)
                .Add("Maximized_ShouldReturnFormWindowStateMaximized", () =>
                    WdUIUsage.ToWindowState("Maximized") == FormWindowState.Maximized)
                .Add("Minimized_ShouldReturnFormWindowStateMinimized", () =>
                    WdUIUsage.ToWindowState("Minimized") == FormWindowState.Minimized)
                .Add("Invalid_ShouldReturnNormal", () =>
                    WdUIUsage.ToWindowState("Invalid") == FormWindowState.Normal)
                .Add("Empty_ShouldReturnNormal", () =>
                    WdUIUsage.ToWindowState("") == FormWindowState.Normal)
                .Add("CaseInsensitive_ShouldWork", () =>
                    WdUIUsage.ToWindowState("maximized") == FormWindowState.Maximized)
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ParseColor()
        {
            var result = new TestGroupBuilder("ParseColor")
                .Add("Red_ByName_ShouldReturnKnownColor", () =>
                    WdUIUsage.ParseColor("Red").Name == "Red")
                .Add("Blue_ByName_ShouldReturnKnownColor", () =>
                    WdUIUsage.ParseColor("Blue").Name == "Blue")
                .Add("HexWithHash_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#FF0000").ToArgb() == Color.Red.ToArgb())
                .Add("HexGreen_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#00FF00").ToArgb() == Color.Lime.ToArgb())
                .Add("HexBlue_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#0000FF").ToArgb() == Color.Blue.ToArgb())
                .Add("HexWhite_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#FFFFFF").ToArgb() == Color.White.ToArgb())
                .Add("HexBlack_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#000000").ToArgb() == Color.Black.ToArgb())
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ParseEnum()
        {
            var result = new TestGroupBuilder("ParseEnum")
                .Add("ValidValue_ShouldReturnParsed", () =>
                    WdUIUsage.ParseEnum("Top", NavigationBarPosition.Top) == NavigationBarPosition.Top)
                .Add("ValidValue_Bottom_ShouldReturnParsed", () =>
                    WdUIUsage.ParseEnum("Bottom", NavigationBarPosition.Top) == NavigationBarPosition.Bottom)
                .Add("CaseInsensitive_ShouldWork", () =>
                    WdUIUsage.ParseEnum("hidden", NavigationBarPosition.Top) == NavigationBarPosition.Hidden)
                .Add("InvalidValue_ShouldReturnDefault", () =>
                    WdUIUsage.ParseEnum("Invalid", NavigationBarPosition.Top) == NavigationBarPosition.Top)
                .Add("EmptyString_ShouldReturnDefault", () =>
                    WdUIUsage.ParseEnum("", NavigationBarPosition.Left) == NavigationBarPosition.Left)
                .Add("NumericString_ShouldReturnParsed", () =>
                    WdUIUsage.ParseEnum("123", NavigationBarPosition.Bottom) == (NavigationBarPosition)123)
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_TruncateString()
        {
            var result = new TestGroupBuilder("TruncateString")
                .Add("ShortString_ShouldReturnOriginal", () =>
                    WdUIUsage.TruncateString("Hello", 10) == "Hello")
                .Add("ExactLength_ShouldReturnOriginal", () =>
                    WdUIUsage.TruncateString("Hello", 5) == "Hello")
                .Add("LongString_ShouldTruncateWithSuffix", () =>
                    WdUIUsage.TruncateString("Hello World", 8) == "Hello...")
                .Add("NullInput_ShouldReturnEmpty", () =>
                    WdUIUsage.TruncateString(null!, 10) == string.Empty)
                .Add("EmptyInput_ShouldReturnEmpty", () =>
                    WdUIUsage.TruncateString("", 10) == string.Empty)
                .Add("MaxLengthZero_ShouldReturnEmpty", () =>
                    WdUIUsage.TruncateString("Hello", 0) == string.Empty)
                .Add("MaxLengthNegative_ShouldReturnEmpty", () =>
                    WdUIUsage.TruncateString("Hello", -1) == string.Empty)
                .Add("MaxLengthLessThanSuffix_ShouldReturnSuffixTruncated", () =>
                    WdUIUsage.TruncateString("Hello World", 2) == "..")
                .Add("CustomSuffix_ShouldUseCustomSuffix", () =>
                    WdUIUsage.TruncateString("Hello World", 8, "..") == "Hello ..")
                .Add("CustomSuffixExactLength_ShouldReturnOriginal", () =>
                    WdUIUsage.TruncateString("Hi", 2, "..") == "Hi")
                .Add("MaxLengthEqualsSuffixLength_ShouldReturnSuffix", () =>
                    WdUIUsage.TruncateString("Hello", 3, "...") == "...")
                .Run();

            result.AssertAllPassed(printSummary: true);
        }
    }
}
