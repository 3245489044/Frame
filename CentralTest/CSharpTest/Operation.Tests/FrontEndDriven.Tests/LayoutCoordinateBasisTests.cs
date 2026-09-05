#nullable enable

using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.LayoutAnalysis.UIInspector;

namespace FrontEndDriven.Tests;

/// <summary>
/// 布局坐标基准回归（框架级修复）：嵌套 scope 宿主不在 form 原点（如 contentContainer 在 mainSplitter.Panel2）时，
/// ComputedLayoutComponent 应为「全局 form 相对」坐标（Left/Top 含宿主偏移），经 ToParentRelative 换算父相对应用坐标，
/// 否则根节点/子节点会被按错误原点放置（拉伸/重排后错位）。
/// 本组用例固化 ToParentRelative 的契约：全局 form 坐标 → 父相对 = 局部偏移。
/// </summary>
[TestClass]
public class LayoutCoordinateBasisTests
{
    /// <summary>构造 Form → ContainerView → root → MainView → mainSplitter(Panel2 在 form 1042,43) → host(Dock=Fill) → child。</summary>
    private static (Form Form, SplitContainer MainSplitter, Panel Host, Panel Child) BuildChain()
    {
        var form = new Form { ClientSize = new Size(1400, 900) };
        var containerView = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        form.Controls.Add(containerView);

        var root = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        containerView.Controls.Add(root);

        var mainView = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        root.Controls.Add(mainView);

        var mainSplitter = new SplitContainer
        {
            Location = new Point(0, 43),
            Size = new Size(1384, 817),
            SplitterDistance = 1038,
            Orientation = Orientation.Vertical,
        };
        mainView.Controls.Add(mainSplitter);

        var host = new Panel { Location = new Point(0, 0), Size = new Size(342, 817), Dock = DockStyle.Fill };
        mainSplitter.Panel2.Controls.Add(host);

        var child = new Panel { Location = new Point(100, 50), Size = new Size(200, 100) };
        host.Controls.Add(child);

        return (form, mainSplitter, host, child);
    }

    [TestMethod]
    public void NestedHostFormPosition_IsNonZero()
    {
        var (_, mainSplitter, host, _) = BuildChain();
        var hostForm = LayoutCoordinateResolver.ToFormRelativeAccurate(host);

        Assert.AreEqual(new Point(1042, 43), hostForm.Location,
            "嵌套宿主（mainSplitter.Panel2 内）form 相对位置应为 (1042,43)——非零即触发坐标基准缺陷");
    }

    [TestMethod]
    public void GlobalFormCoords_ToParentRelative_EqualsLocalOffset()
    {
        var (_, _, host, child) = BuildChain();

        // 宿主 form 位置 (1042,43)；子控件局部偏移 (100,50)。
        var hostForm = LayoutCoordinateResolver.ToFormRelativeAccurate(host);
        int globalX = hostForm.X + 100;
        int globalY = hostForm.Y + 50;

        // 框架级修复后：ComputedLayoutComponent.Left/Top = 全局 form 相对（含宿主偏移），
        // ToParentRelative 应还原为相对宿主 (= 局部偏移)，而非被宿主全局位置偏移错放。
        var rel = LayoutCoordinateResolver.ToParentRelative(child, globalX, globalY);

        Assert.AreEqual(new Point(100, 50), rel,
            "全局 form 坐标经 ToParentRelative 应还原为父相对局部偏移（坐标基准一致）；否则根节点/子节点重排错位");
    }
}
