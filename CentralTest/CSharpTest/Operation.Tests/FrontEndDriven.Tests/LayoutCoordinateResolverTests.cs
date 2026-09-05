
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.LayoutAnalysis.UIInspector;

namespace FrontEndDriven.Tests;

/// <summary>
/// LayoutCoordinateResolver 真实父链集成测试（R2 回归排查）：
/// 验证 ToParentRelative 在「父已正确 SetBounds + SplitterDistance 已设置」场景下的换算结果，
/// 与 MainLayout.json 实际层级一致（Form → ContainerView → root → MainView → mainSplitter → contentSplitter → terminalPanel）。
/// 若此处通过而 GUI 仍错位 → 问题在 ApplyProperties 调用时序/值，需专项日志。
/// </summary>
[TestClass]
public class LayoutCoordinateResolverTests
{
    /// <summary>构造 MainLayout 完整父链（与配置层级一致）。</summary>
    private static (Form Form, Panel Root, Panel MainView, SplitContainer MainSplitter, SplitContainer ContentSplitter, Panel TerminalPanel) BuildChain()
    {
        var form = new Form { ClientSize = new Size(1400, 900) };
        var containerView = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        form.Controls.Add(containerView);

        var root = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        containerView.Controls.Add(root);

        var mainView = new Panel { Location = new Point(0, 0), Size = new Size(1384, 861) };
        root.Controls.Add(mainView);

        var toolbar = new Panel { Location = new Point(0, 0), Size = new Size(1384, 43) };
        mainView.Controls.Add(toolbar);

        var mainSplitter = new SplitContainer
        {
            Location = new Point(0, 43),
            Size = new Size(1384, 817),
            SplitterDistance = 1038, // 75% * 1384（MainLayout.json splitterDistance=0.75）
            Orientation = Orientation.Vertical,
        };
        mainView.Controls.Add(mainSplitter);

        var contentSplitter = new SplitContainer
        {
            Location = new Point(0, 0),
            Size = new Size(1038, 817),
            SplitterDistance = 622, // 60% * 1038（MainLayout.json splitterDistance=0.60）
            Orientation = Orientation.Vertical,
        };
        mainSplitter.Panel1.Controls.Add(contentSplitter);

        var terminalPanel = new Panel { Location = new Point(0, 0), Size = new Size(412, 817) };
        contentSplitter.Panel2.Controls.Add(terminalPanel);

        return (form, root, mainView, mainSplitter, contentSplitter, terminalPanel);
    }

    [TestMethod]
    public void ToParentRelative_ContentSplitter_ReturnsRelativeToPanel1()
    {
        var (form, _, _, mainSplitter, contentSplitter, _) = BuildChain();

        // mainSplitter 已 SetBounds(0,43)（相对 MainView，form 基准），Panel1 form=(0,43)。
        // contentSplitter ECS form 相对 (0,43) → 相对 Panel1 应为 (0,0)。
        var rel = LayoutCoordinateResolver.ToParentRelative(contentSplitter, 0, 43);

        Assert.AreEqual(new Point(0, 0), rel, "contentSplitter 相对 mainSplitter_Panel1 应为 (0,0)");
        // E-2（8.6）修正：SplitterPanel.Location 平台语义恒为 (0,0)（位置由 SplitContainer 管理），
        // 原断言值 (0,43) 为手算错误，与断言消息 "(0,0)" 自相矛盾。
        Assert.AreEqual(new Point(0, 0), mainSplitter.Panel1.Location, "Panel1 相对 mainSplitter 应为 (0,0)（SplitterPanel 由 SplitContainer 管理）");
    }

    [TestMethod]
    public void ToParentRelative_TerminalPanel_ReturnsRelativeToPanel2()
    {
        var (form, _, _, mainSplitter, contentSplitter, terminalPanel) = BuildChain();

        // terminalPanel ECS form 相对 (626,43)：contentSplitter form=(0,43)，Panel2 form = (0+622+4, 43) = (626,43)，
        // 相对 Panel2 应为 (0,0)。
        var rel = LayoutCoordinateResolver.ToParentRelative(terminalPanel, 626, 43);

        // E-2（8.6）修正：Panel2 form=(626,43) 与 ECS form 相对输入 (626,43) 差值 = (0,0)，
        // 原断言值 (0,43) 为手算错误（将 contentSplitter 的 Y=43 误当差值）。
        Assert.AreEqual(new Point(0, 0), rel, "terminalPanel 相对 contentSplitter_Panel2 应为 (0,0)");
    }

    [TestMethod]
    public void ToFormRelativeAccurate_TerminalPanel_MatchesChain()
    {
        var (form, _, _, _, _, terminalPanel) = BuildChain();

        var rect = LayoutCoordinateResolver.ToFormRelativeAccurate(terminalPanel);

        Assert.AreEqual(new Point(626, 43), rect.Location, "terminalPanel form 相对位置应为 (626,43)");
    }
}
