using System.Collections;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParameterResolver.Utilities;
using WinFormsValueContract.Sources;

namespace ParameterResolver.Tests.Utilities;

/// <summary>
/// 属性路径解析内核单测——覆盖嵌套属性/索引段/缺失路径/越界索引/大小写（D1 收敛回归防护）。
/// </summary>
[TestClass]
public class PropertyPathResolverTests
{
    private sealed class SampleNode
    {
        public string Name { get; set; } = "";

        public SampleNode? Child { get; set; }

        public ArrayList Items { get; } = [];
    }

    private static SampleNode CreateTree()
    {
        var root = new SampleNode { Name = "root" };
        var child = new SampleNode { Name = "child" };
        root.Child = child;
        child.Items.Add(new SampleNode { Name = "item0" });
        child.Items.Add(new SampleNode { Name = "item1" });
        return root;
    }

    [TestMethod]
    public void ReadNestedProperty_SimplePath_ShouldReturnValue()
    {
        var root = CreateTree();
        Assert.AreEqual("root", PropertyPathResolver.ReadNestedProperty(root, "Name"));
    }

    [TestMethod]
    public void ReadNestedProperty_NestedPath_ShouldReturnChildValue()
    {
        var root = CreateTree();
        Assert.AreEqual("child", PropertyPathResolver.ReadNestedProperty(root, "Child.Name"));
    }

    [TestMethod]
    public void ReadNestedProperty_IndexSegment_ShouldReturnIndexedItem()
    {
        var root = CreateTree();
        // 模拟 ListView 参数绑定路径: SelectedItems.0.Tag 语义（索引段 + 属性段混合）
        Assert.AreEqual("item1", PropertyPathResolver.ReadNestedProperty(root, "Child.Items.1.Name"));
    }

    [TestMethod]
    public void ReadNestedProperty_MissingProperty_ShouldReturnNull()
    {
        var root = CreateTree();
        Assert.IsNull(PropertyPathResolver.ReadNestedProperty(root, "Child.NoSuchProperty"));
    }

    [TestMethod]
    public void ReadNestedProperty_IndexOutOfRange_ShouldReturnNull()
    {
        var root = CreateTree();
        Assert.IsNull(PropertyPathResolver.ReadNestedProperty(root, "Child.Items.99.Name"));
    }

    [TestMethod]
    public void ReadNestedProperty_NullRoot_ShouldReturnNull()
    {
        Assert.IsNull(PropertyPathResolver.ReadNestedProperty(null, "Name"));
    }

    [TestMethod]
    public void ReadNestedProperty_EmptyPath_ShouldReturnNull()
    {
        var root = CreateTree();
        Assert.IsNull(PropertyPathResolver.ReadNestedProperty(root, ""));
    }
}

/// <summary>
/// 控件取值内核单测——覆盖 ListView 选中项特判与 Text/Value 回退（D5 收敛回归防护）。
/// </summary>
[TestClass]
public class ControlValueAccessorTests
{
    [TestMethod]
    public void GetControlValue_ListView_NoSelection_ShouldReturnNull()
    {
        using var listView = new ListView();
        Assert.IsNull(ControlValueAccessor.GetControlValue(listView));
    }

    [TestMethod]
    public void GetControlValue_ListView_Selected_ShouldReturnTagPreferText()
    {
        using var listView = new ListView();
        var item = new ListViewItem("显示文本") { Tag = "tag-value" };
        listView.Items.Add(item);
        // ListView 选中项依赖物理显示（Handle 已创建）：无 Handle 时 SelectedItems 恒空（诊断实证，同报告 P1）
        _ = listView.Handle;
        item.Selected = true;

        Assert.AreEqual("tag-value", ControlValueAccessor.GetControlValue(listView));
    }

    [TestMethod]
    public void GetControlValue_ListView_Selected_NoTag_ShouldReturnText()
    {
        using var listView = new ListView();
        var item = new ListViewItem("仅文本");
        listView.Items.Add(item);
        _ = listView.Handle;
        item.Selected = true;

        Assert.AreEqual("仅文本", ControlValueAccessor.GetControlValue(listView));
    }

    [TestMethod]
    public void GetControlValue_Button_ShouldReturnText()
    {
        using var button = new Button { Text = "按钮文本" };
        Assert.AreEqual("按钮文本", ControlValueAccessor.GetControlValue(button));
    }

    [TestMethod]
    public void GetControlValue_Null_ShouldReturnNull()
    {
        Assert.IsNull(ControlValueAccessor.GetControlValue(null!));
    }
}
