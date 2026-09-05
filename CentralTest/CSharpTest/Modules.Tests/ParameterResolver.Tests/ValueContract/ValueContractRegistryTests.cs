using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;
using TConfig.Controls;
using WinFormsValueContract;

namespace ParameterResolver.Tests.ValueContract;

/// <summary>
/// 值语义契约层单测——覆盖 Selection/Text/Checked 语义读写、写后规范值回读（ListView 匹配选中→Tag 完整路径）、
/// 变更信号查询与基类回退（P1 契约层回归防护：类型差异全部下沉到契约，消费方不再散落控件类型判断）。
/// </summary>
[TestClass]
public class ValueContractRegistryTests
{
    private readonly ControlValueBridge _bridge = new();

    // ── ListView Selection ──

    [TestMethod]
    public void ListView_DefaultSemantic_IsSelection()
    {
        using var lv = new ListView();
        var contract = ValueContractRegistry.GetForType(typeof(ListView));
        Assert.IsNotNull(contract);
        Assert.AreEqual(ValueSemantic.Selection, contract!.DefaultSemantic);
    }

    [TestMethod]
    public void ListView_Selection_WriteByShortName_MatchesTag_ReturnsCanonicalPath()
    {
        using var lv = new ListView();
        _ = lv.Handle;
        lv.Items.Add(new ListViewItem("MathFunctions") { Tag = @"D:\cfg\MathFunctions.json" });
        lv.Items.Add(new ListViewItem("WorkflowInputUI") { Tag = @"D:\cfg\WorkflowInputUI.json" });

        // 脚本 set 语义：短名匹配 Tag 包含 → 选中 → 回读完整路径（写后规范值）
        var canonical = _bridge.SetValue(lv, "MathFunctions");

        Assert.AreEqual(@"D:\cfg\MathFunctions.json", canonical);
        Assert.AreEqual(1, lv.SelectedItems.Count);
        Assert.AreEqual(@"D:\cfg\MathFunctions.json", _bridge.GetValue(lv));
    }

    [TestMethod]
    public void ListView_Selection_Write_IsSingleSelect_ClearsPriorSelection()
    {
        // 回归：LoadJsonConfigList 默认选中首项后，set 匹配他项必须单选（SelectedItems[0] 唯一且为匹配项），
        // 否则 property 物理读 SelectedItems.0.Tag 读到首项（AgentConfigUI 误替 MathFunctions 缺陷根因）。
        using var lv = new ListView();
        _ = lv.Handle;
        lv.Items.Add(new ListViewItem("AgentConfigUI") { Tag = @"D:\cfg\AgentConfigUI.json" });
        lv.Items.Add(new ListViewItem("MathFunctions") { Tag = @"D:\cfg\MathFunctions.json" });
        lv.Items[0].Selected = true;

        var canonical = _bridge.SetValue(lv, "MathFunctions");

        Assert.AreEqual(@"D:\cfg\MathFunctions.json", canonical);
        Assert.AreEqual(1, lv.SelectedItems.Count);
        Assert.AreEqual(@"D:\cfg\MathFunctions.json", lv.SelectedItems[0].Tag);
        Assert.AreEqual(@"D:\cfg\MathFunctions.json", _bridge.GetValue(lv));
    }

    [TestMethod]
    public void ListView_Selection_Read_NoSelection_ReturnsNull()
    {
        using var lv = new ListView();
        _ = lv.Handle;
        Assert.IsNull(_bridge.GetValue(lv));
    }

    [TestMethod]
    public void ListView_Content_Write_ReloadsItems()
    {
        using var lv = new ListView();
        _ = lv.Handle;
        var canonical = _bridge.SetValue(lv, "A\nB\nC", ValueSemantic.Content);

        Assert.IsNull(canonical); // Content 无单一规范值
        Assert.AreEqual(3, lv.Items.Count);
        Assert.AreEqual("A\nB\nC", _bridge.GetValue(lv, ValueSemantic.Content));
    }

    // ── ComboBox Selection ──

    [TestMethod]
    public void ComboBox_Selection_WriteByText_ReturnsCanonical()
    {
        using var cb = new ComboBox();
        cb.Items.AddRange(new object[] { "A", "B", "C" });

        var canonical = _bridge.SetValue(cb, "B");

        Assert.AreEqual("B", canonical);
        Assert.AreEqual("B", _bridge.GetValue(cb));
    }

    [TestMethod]
    public void ComboBox_Selection_WriteByIndex_ReturnsCanonical()
    {
        using var cb = new ComboBox();
        cb.Items.AddRange(new object[] { "A", "B", "C" });

        var canonical = _bridge.SetValue(cb, "2");

        Assert.AreEqual("C", canonical);
        Assert.AreEqual("C", cb.SelectedItem);
    }

    // ── CheckBox Checked ──

    [TestMethod]
    public void CheckBox_Checked_WriteTrue_ReturnsCanonical()
    {
        using var ck = new CheckBox();
        var canonical = _bridge.SetValue(ck, "true");

        Assert.AreEqual("True", canonical);
        Assert.IsTrue(ck.Checked);
        Assert.AreEqual(true, _bridge.GetValue(ck)); // GetValue 返回原生 bool（非字符串）
    }

    // ── TextBox Text ──

    [TestMethod]
    public void TextBox_Text_Write_ReturnsCanonical()
    {
        using var tb = new TextBox();
        var canonical = _bridge.SetValue(tb, "hello");

        Assert.AreEqual("hello", canonical);
        Assert.AreEqual("hello", tb.Text);
        Assert.AreEqual("hello", _bridge.GetValue(tb));
    }

    // ── TextBox 换行显示规范化（框架缺陷回归：跨层文本写入统一经 ControlTextNormalizer）──

    [TestMethod]
    public void TextBox_Text_Write_LfBecomesCrlf_AndEnablesMultiline()
    {
        using var tb = new TextBox();
        var canonical = _bridge.SetValue(tb, "line1\nline2");

        Assert.AreEqual("line1\r\nline2", canonical);
        Assert.AreEqual("line1\r\nline2", tb.Text);
        Assert.IsTrue(tb.Multiline);
    }

    [TestMethod]
    public void TextBox_Text_Write_AlreadyCrlf_StaysCrlf()
    {
        using var tb = new TextBox();
        var canonical = _bridge.SetValue(tb, "a\r\nb");

        Assert.AreEqual("a\r\nb", canonical);
        Assert.AreEqual("a\r\nb", tb.Text);
        Assert.IsTrue(tb.Multiline);
    }

    [TestMethod]
    public void TextBox_Text_Write_SingleLine_KeepsSingleLine()
    {
        using var tb = new TextBox();
        _bridge.SetValue(tb, "hello");

        Assert.IsFalse(tb.Multiline);
    }

    [TestMethod]
    public void NormalizeForDisplay_LfAndMixedLineEndings_UnifyToCrlf()
    {
        Assert.AreEqual("a\r\nb\r\nc", ControlTextNormalizer.NormalizeForDisplay("a\nb\nc"));
        Assert.AreEqual("a\r\nb", ControlTextNormalizer.NormalizeForDisplay("a\r\nb"));
        Assert.AreEqual("a\r\nb\r\nc", ControlTextNormalizer.NormalizeForDisplay("a\r\nb\nc"));
        Assert.IsNull(ControlTextNormalizer.NormalizeForDisplay(null));
        Assert.AreEqual("", ControlTextNormalizer.NormalizeForDisplay(""));
    }

    // ── 变更信号 ──

    [TestMethod]
    public void GetValueChangedEvent_ListView_Selection_ReturnsSelectedIndexChanged()
    {
        using var lv = new ListView();
        Assert.AreEqual(nameof(ListView.SelectedIndexChanged), _bridge.GetValueChangedEvent(lv, ValueSemantic.Selection));
    }

    [TestMethod]
    public void GetValueChangedEvent_CheckBox_Checked_ReturnsCheckedChanged()
    {
        using var ck = new CheckBox();
        Assert.AreEqual(nameof(CheckBox.CheckedChanged), _bridge.GetValueChangedEvent(ck, ValueSemantic.Checked));
    }

    [TestMethod]
    public void GetValueChangedEvent_UnregisteredSemantic_ReturnsNull()
    {
        using var tb = new TextBox();
        Assert.IsNull(_bridge.GetValueChangedEvent(tb, ValueSemantic.Selection));
    }

    // ── 基类回退 ──

    private sealed class CustomTextBox : TextBox
    {
    }

    [TestMethod]
    public void GetForType_UnregisteredSubclass_FallsBackToBaseContract()
    {
        using var custom = new CustomTextBox();
        Assert.IsTrue(_bridge.IsSupported(custom));

        _bridge.SetValue(custom, "x");
        Assert.AreEqual("x", custom.Text);
    }

    // ── 未注册类型 ──

    [TestMethod]
    public void IsSupported_UnregisteredType_ReturnsFalse()
    {
        Assert.IsFalse(_bridge.IsSupported(new object()));
        Assert.IsFalse(_bridge.IsSupported(null!));
    }

    // ── HtmlViewer 主题指令（阶段 6 ThemeMode 参数通道；@theme 只设属性、不触发 WebView2 渲染）──

    [TestMethod]
    public void HtmlViewer_ThemeDirective_Dark_SetsThemeMode_WithoutRendering()
    {
        using var hv = new HtmlViewer();
        var canonical = _bridge.SetValue(hv, "@theme:dark");
        Assert.AreEqual(ThemeMode.Dark, hv.ThemeMode);
        Assert.AreEqual("@theme:dark", canonical);
    }

    [TestMethod]
    public void HtmlViewer_ThemeDirective_LightCaseInsensitive_SetsThemeMode()
    {
        using var hv = new HtmlViewer();
        _bridge.SetValue(hv, "@Theme: Light");
        Assert.AreEqual(ThemeMode.Light, hv.ThemeMode);
    }

    [TestMethod]
    public void HtmlViewer_ThemeDirective_System_SetsSystem()
    {
        using var hv = new HtmlViewer();
        _bridge.SetValue(hv, "@theme: system");
        Assert.AreEqual(ThemeMode.System, hv.ThemeMode);
    }
}
