#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Protocol;
using FrontEndDriven.Shared.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// F5·写路径 ECS 单一权威单测：PropertyApplier 在提供 ECS 父原点时，
/// 用「全局 form 坐标 − ECS 父原点」写父相对 Bounds（纯 ECS 单一权威）；
/// 未提供时回退物理父链 ToParentRelative。两分支在 ECS/物理一致时结果一致——
/// ECS 父原点为有效替代，坐标写源统一、消除「物理父链未就位」漂移。
/// </summary>
[TestClass]
public class PropertyApplierF5Tests
{
    private sealed class RecordingAdapter : IControlAdapter
    {
        public (int Left, int Top, int Width, int Height)? LastBounds;

        public void SetBounds(object control, int left, int top, int width, int height)
            => LastBounds = (left, top, width, height);

        public Size GetParentClientSize(object control) => throw new NotSupportedException();
        public object? GetParent(object control) => throw new NotSupportedException();
        public int GetLeft(object control) => throw new NotSupportedException();
        public int GetTop(object control) => throw new NotSupportedException();
        public int GetWidth(object control) => throw new NotSupportedException();
        public int GetHeight(object control) => throw new NotSupportedException();
        public void SetFont(object control, string fontFamily, float fontSize, bool bold, bool italic, bool underline) { }
        public void SetForeColor(object control, string htmlColor) { }
        public void SetBackColor(object control, string htmlColor) { }
        public bool AddControl(object parent, object child) => throw new NotSupportedException();
        public bool Contains(object parent, object child) => throw new NotSupportedException();
        public void RemoveControl(object parent, object child) { }
        public void ClearControls(object parent) { }
        public void SuspendLayout(object control) { }
        public void ResumeLayout(object control, bool performLayout) { }
        public string GetText(object control) => string.Empty;
        public void SetText(object control, string text) { }
        public bool GetEnabled(object control) => true;
        public void SetEnabled(object control, bool enabled) { }
        public bool GetVisible(object control) => true;
        public void SetVisible(object control, bool visible) { }
        public string? GetName(object control) => null;
        public int GetSplitterDistance(object splitContainer) => throw new NotSupportedException();
        public void SetSplitterDistance(object splitContainer, int distance) { }
        public Orientation GetOrientation(object splitContainer) => throw new NotSupportedException();
        public object? GetSplitterPanel(object splitContainer, string panelSide) => throw new NotSupportedException();
        public Action SubscribeSplitterMoved(object splitContainer, EventHandler handler) => () => { };
        public void SetBorderStyle(object control, int borderWidth, string borderColor) { }
        public System.Collections.IEnumerable GetControls(object parent) => Array.Empty<object>();
        public object? FindForm(object control) => null;
        public bool IsDisposed(object control) => false;
        public bool GetInvokeRequired(object control) => false;
    }

    private static Dictionary<string, object?> BoundsProps(int left, int top, int width, int height)
        => new()
        {
            [UIProtocol.Prop_Left] = left,
            [UIProtocol.Prop_Top] = top,
            [UIProtocol.Prop_Width] = width,
            [UIProtocol.Prop_Height] = height,
        };

    [TestMethod]
    public void ApplyProperties_WithEcsParentOrigin_UsesEcsSingleAuthority()
    {
        var control = new Panel();
        var adapter = new RecordingAdapter();

        // 全局 form 坐标 (1200,500)，ECS 父原点 (1100,450) → 父相对 (100,50)。
        PropertyApplier.ApplyProperties(control, BoundsProps(1200, 500, 200, 100), adapter, new Point(1100, 450));

        Assert.AreEqual((100, 50, 200, 100), adapter.LastBounds,
            "提供 ECS 父原点时，父相对 = 全局 form 坐标 − ECS 父原点（纯 ECS 单一权威）");
    }

    [TestMethod]
    public void ApplyProperties_WithoutEcsParentOrigin_FallsBackToPhysicalParentChain()
    {
        // 物理父链：parent 非根（其 Parent=root），故 GetFormRelativeLocation(parent) = parent.Location (1100,450)。
        var root = new Panel { Location = new Point(0, 0) };
        var parent = new Panel { Location = new Point(1100, 450) };
        root.Controls.Add(parent);
        var control = new Panel { Location = new Point(0, 0) };
        parent.Controls.Add(control);
        var adapter = new RecordingAdapter();

        // 未提供 ECS 父原点 → 回退物理父链 ToParentRelative（父物理 form 位置 (1100,450) → 父相对 (100,50)）。
        PropertyApplier.ApplyProperties(control, BoundsProps(1200, 500, 200, 100), adapter);

        Assert.AreEqual((100, 50, 200, 100), adapter.LastBounds,
            "未提供 ECS 父原点时，回退物理父链 ToParentRelative 换算（契约不变）");
    }
}
