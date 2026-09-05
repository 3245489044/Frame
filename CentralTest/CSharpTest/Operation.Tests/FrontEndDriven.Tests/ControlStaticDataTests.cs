using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CommandFramework.Extensions;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS.Components;

namespace FrontEndDriven.Tests;

/// <summary>
/// 静态数据子系统（ControlStaticDataComponent 捕获 + ControlStaticDataApplier 应用）纯数据单测。
/// 覆盖 schema 已声明但旧管线整体丢弃的静态属性（items/selectedIndex/checkedItems/columns/view/dropDownStyle/checked）：
///   ① 捕获：ControlFactory.CreateFromConfig 从 ControlConfig 写入组件（单一权威）；
///   ② 应用：ControlStaticDataApplier 按控件类型把组件应用物理控件（创建期，与 ControlBehaviorComponent 同构）；
///   ③ 守卫：HasApplier 声明即覆盖（有静态数据但无应用器的类型 → 显式告警，不再静默丢弃）。
/// </summary>
[TestClass]
public class ControlStaticDataTests
{
    private EntityManager _entityManager = null!;
    private ControlFactory _controlFactory = null!;

    [TestInitialize]
    public void Setup()
    {
        IECSStorage storage = new MockECSStorage();
        IStorageRegistry registry = new MockStorageRegistry();
        _entityManager = new EntityManager(storage, registry);
        _controlFactory = new ControlFactory(_entityManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _entityManager.Dispose();
    }

    // ── 捕获：ControlFactory.CreateFromConfig → ControlStaticDataComponent ──

    [TestMethod]
    public void CreateFromConfig_Combobox_WithStaticData_CapturesComponent()
    {
        var config = new ControlConfig
        {
            Type = "combobox",
            Name = "jsonTemplateComboBox",
            Items = new List<string> { "run.llm", "runLoop.hybrid", "runLoop.config" },
            SelectedIndex = 1,
            DropDownStyle = "DropDownList"
        };

        var entity = _controlFactory.CreateFromConfig(config);

        Assert.IsTrue(_entityManager.HasComponent<ControlStaticDataComponent>(entity));
        var data = _entityManager.GetComponent<ControlStaticDataComponent>(entity);
        CollectionAssert.AreEqual(
            new[] { "run.llm", "runLoop.hybrid", "runLoop.config" },
            data.Items?.ToArray());
        Assert.AreEqual(1, data.SelectedIndex);
        Assert.AreEqual("DropDownList", data.DropDownStyle);
    }

    [TestMethod]
    public void CreateFromConfig_CheckedListBox_WithCheckedItems_CapturesComponent()
    {
        var config = new ControlConfig
        {
            Type = "checkedlistbox",
            Name = "clbFeatures",
            Items = new List<string> { "a", "b", "c" },
            CheckedItems = new List<string> { "a", "c" }
        };

        var entity = _controlFactory.CreateFromConfig(config);

        var data = _entityManager.GetComponent<ControlStaticDataComponent>(entity);
        CollectionAssert.AreEqual(new[] { "a", "c" }, data.CheckedItems?.ToArray());
    }

    [TestMethod]
    public void CreateFromConfig_WithoutStaticData_DoesNotAddComponent()
    {
        var config = new ControlConfig { Type = "button", Name = "btnSubmit" };

        var entity = _controlFactory.CreateFromConfig(config);

        Assert.IsFalse(_entityManager.HasComponent<ControlStaticDataComponent>(entity));
    }

    // ── 应用：ControlStaticDataApplier → 物理控件 ──

    [TestMethod]
    public void Apply_ComboBox_AppliesItemsStyleAndSelection()
    {
        var combo = new ComboBox();
        var data = new ControlStaticDataComponent
        {
            Items = new List<string> { "run.llm", "runLoop.hybrid", "runLoop.config" },
            SelectedIndex = 1,
            DropDownStyle = "DropDownList"
        };

        ControlStaticDataApplier.Apply("combobox", combo, data);

        Assert.AreEqual(3, combo.Items.Count);
        Assert.AreEqual("runLoop.hybrid", combo.SelectedItem?.ToString());
        Assert.AreEqual(ComboBoxStyle.DropDownList, combo.DropDownStyle);
    }

    [TestMethod]
    public void Apply_ComboBox_WithoutSelectedIndex_LeavesNoSelection()
    {
        var combo = new ComboBox();
        var data = new ControlStaticDataComponent
        {
            Items = new List<string> { "run.llm", "runLoop.hybrid" }
        };

        ControlStaticDataApplier.Apply("combobox", combo, data);

        Assert.AreEqual(2, combo.Items.Count);
        Assert.AreEqual(-1, combo.SelectedIndex, "未声明 selectedIndex 时下拉应无默认选中（配置语义）");
    }

    [TestMethod]
    public void Apply_CheckedListBox_AppliesItemsAndCheckedItems()
    {
        var clb = new CheckedListBox();
        var data = new ControlStaticDataComponent
        {
            Items = new List<string> { "a", "b", "c" },
            CheckedItems = new List<string> { "a", "c" }
        };

        ControlStaticDataApplier.Apply("checkedlistbox", clb, data);

        Assert.AreEqual(3, clb.Items.Count);
        Assert.IsTrue(clb.GetItemChecked(0));
        Assert.IsFalse(clb.GetItemChecked(1));
        Assert.IsTrue(clb.GetItemChecked(2));
    }

    [TestMethod]
    public void Apply_ListView_AppliesColumns()
    {
        var listView = new ListView { View = View.Details };
        var data = new ControlStaticDataComponent
        {
            Columns = new List<ListViewColumnConfig>
            {
                new() { Text = "Name", Width = 120 },
                new() { Text = "Age", Width = 80 }
            }
        };

        ControlStaticDataApplier.Apply("listview", listView, data);

        Assert.AreEqual(2, listView.Columns.Count);
        Assert.AreEqual("Name", listView.Columns[0].Text);
        Assert.AreEqual(80, listView.Columns[1].Width);
    }

    // ── 守卫：声明即覆盖（有静态数据但无应用器的类型 → 显式暴露） ──

    [TestMethod]
    public void HasApplier_CoveredTypes_ReturnsTrue()
    {
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("combobox"));
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("listbox"));
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("checkedlistbox"));
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("domainupdown"));
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("listview"));
        Assert.IsTrue(ControlStaticDataApplier.HasApplier("checkbox"));
    }

    [TestMethod]
    public void AddOrSetComponent_Twice_DoesNotThrow()
    {
        var config = new ControlConfig { Type = "textbox", Name = "txtOutput" };
        var entity = _controlFactory.CreateFromConfig(config);

        // 第一次：AddComponent（实体尚无 ControlValueComponent）
        _entityManager.AddOrSetComponent(entity, new ControlValueComponent { Value = "first" });

        // 第二次：HasComponent=true → SetComponent（不应抛「already has component」）
        _entityManager.AddOrSetComponent(entity, new ControlValueComponent { Value = "second" });

        var value = _entityManager.GetComponent<ControlValueComponent>(entity).Value;
        Assert.AreEqual("second", value);
    }

    [TestMethod]
    public void AddOrSetComponent_Concurrent_DoesNotThrow()
    {
        var config = new ControlConfig { Type = "textbox", Name = "txtConcurrent" };
        var entity = _controlFactory.CreateFromConfig(config);

        // 并发写同一实体（模拟「事件输出回写 + 物理输入回灌」同时 SetValue）：
        // 原子 AddOrSet 在单写锁内检查+写入 → 不应抛「already has component」，最终组件必存在。
        var tasks = Enumerable.Range(0, 8)
            .Select(i => System.Threading.Tasks.Task.Run(() =>
                _entityManager.AddOrSetComponent(entity, new ControlValueComponent { Value = $"v{i}" })))
            .ToArray();
        System.Threading.Tasks.Task.WaitAll(tasks);

        Assert.IsTrue(_entityManager.HasComponent<ControlValueComponent>(entity));
        Assert.IsNotNull(_entityManager.GetComponent<ControlValueComponent>(entity).Value);
    }

    [TestMethod]
    public void HasApplier_UncoveredType_ReturnsFalse()
    {
        Assert.IsFalse(ControlStaticDataApplier.HasApplier("picturebox"));
        Assert.IsFalse(ControlStaticDataApplier.HasApplier(""));
    }
}
