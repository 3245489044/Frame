
#nullable enable

using System;
using System.Drawing;

namespace FrontEndDriven.Abstractions
{
    /// <summary>
    /// 控件适配端口（UI 无关抽象）——后端（WinForms/WPF/无头测试）必须提供的能力契约。
    /// 原 <c>IWinFormsControlAdapter</c> 的通用化：签名仅依赖 <see cref="System.Drawing"/> 与
    /// <see cref="EventHandler"/>，不含任何 <c>System.Windows.Forms</c> 类型，故可脱离 WinForms 命名。
    /// 编排内核（Applier/LayoutProjector/PropertyApplier）只依赖本端口，与具体 UI 工具包解耦。
    /// </summary>
    public interface IControlAdapter
    {

        void SetBounds(object control, int left, int top, int width, int height);

        Size GetParentClientSize(object control);

        object? GetParent(object control);

        int GetLeft(object control);

        int GetTop(object control);

        int GetWidth(object control);

        int GetHeight(object control);


        void SetFont(object control, string fontFamily, float fontSize, bool bold, bool italic, bool underline);

        void SetForeColor(object control, string htmlColor);

        void SetBackColor(object control, string htmlColor);


        /// <summary>
        /// 挂载子控件到父控件（挂载成败契约，M1）。
        /// true=子控件已确定性进入目标子集合（TabControl.TabPages / Panel.Controls / SplitterPanel）；
        /// false=未入宿主（父/子不可用、防环拦截、TabPage 无 TabControl 宿主等）。
        /// 调用方不得把"调用"当"挂载事实"——上层须依返回值计数并在物理层二次确认。
        /// </summary>
        bool AddControl(object parent, object child);

        /// <summary>
        /// 物理入宿主判定（M2 挂载判据物理闭环）：child 是否已实际进入 parent 的目标子集合。
        /// 平台适配宿主集合差异（TabControl.TabPages vs Panel/普通控件 Controls）。
        /// 用于对「AddControl 已返回 true」做二次确认，杜绝「调用即成功」的假绿。
        /// </summary>
        bool Contains(object parent, object child);

        void RemoveControl(object parent, object child);

        void ClearControls(object parent);

        void SuspendLayout(object control);

        void ResumeLayout(object control, bool performLayout);


        string GetText(object control);

        void SetText(object control, string text);

        bool GetEnabled(object control);

        void SetEnabled(object control, bool enabled);

        bool GetVisible(object control);

        void SetVisible(object control, bool visible);

        string? GetName(object control);


        int GetSplitterDistance(object splitContainer);

        void SetSplitterDistance(object splitContainer, int distance);

        Orientation GetOrientation(object splitContainer);

        object? GetSplitterPanel(object splitContainer, string panelSide);

        Action SubscribeSplitterMoved(object splitContainer, EventHandler handler);


        void SetBorderStyle(object control, int borderWidth, string borderColor);


        System.Collections.IEnumerable GetControls(object parent);

        object? FindForm(object control);

        bool IsDisposed(object control);

        bool GetInvokeRequired(object control);
    }
}
