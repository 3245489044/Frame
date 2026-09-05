
using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;

namespace FrontEndDriven.Tests;

/// <summary>
/// E5·事件词表一致性测试：<see cref="EventSubscriptionHelper.PhysicallySubscribableEvents"/>（可物理订阅能力集真源）
/// 必须与 <see cref="EventSubscriptionHelper.TrySubscribe"/> 实际分派能力一致——能力集声明的每个事件名都能在
/// 至少一种控件上真实挂接，防止「校验通过但绑定必失败」的词表漂移再次回归。
/// </summary>
[TestClass]
public class EventSubscriptionHelperTests
{
    private sealed class RaisedFlag { public int Count; }

    /// <summary>能力集中每个事件名都能被 TrySubscribe 在至少一种匹配控件上真实挂接。</summary>
    [TestMethod]
    public void PhysicallySubscribableEvents_All_CanBeSubscribed_OnSomeControl()
    {
        var byControl = new Dictionary<string, Control>
        {
            ["click"] = new Button(),
            ["doubleclick"] = new Button(),
            ["textchanged"] = new TextBox(),
            ["checkedchanged"] = new CheckBox(),
            ["selectedindexchanged"] = new ComboBox(),
            ["valuechanged"] = new NumericUpDown(),
            ["afterselect"] = new TreeView(),
            ["selectionchanged"] = new DataGridView(),
            ["message"] = new HtmlViewer(),
        };

        foreach (var eventName in EventSubscriptionHelper.PhysicallySubscribableEvents)
        {
            var flag = new RaisedFlag();
            var subscribed = EventSubscriptionHelper.TrySubscribe(byControl[eventName], eventName, () => flag.Count++);
            Assert.IsTrue(subscribed,
                $"能力集声明的 {eventName} 应能在对应控件上真实物理挂接（否则与 TrySubscribe 分派漂移）");
        }
    }

    /// <summary>能力集声明的每个事件名都确实在标准事件名词表内（语义：是标准事件，仅能力集更窄）。</summary>
    [TestMethod]
    public void PhysicallySubscribableEvents_AreSubset_OfStandardEventTypes()
    {
        foreach (var eventName in EventSubscriptionHelper.PhysicallySubscribableEvents)
        {
            Assert.IsTrue(EventHelper.StandardEventTypes.Contains(eventName),
                $"{eventName} 应在 StandardEventTypes 标准词表内（能力集是标准词表的物理可订阅子集）");
        }
    }

    /// <summary>TrySubscribe 对能力集外的事件返回 false（如标准词表有但分派器未实现的 keydown）。</summary>
    [TestMethod]
    public void TrySubscribe_UnsupportedEvent_ReturnsFalse()
    {
        var control = new Button();
        var subscribed = EventSubscriptionHelper.TrySubscribe(control, "keydown", () => { });
        Assert.IsFalse(subscribed, "keydown 未在能力集内，TrySubscribe 应返回 false");
    }
}
