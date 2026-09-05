
#nullable enable

using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Protocol;
using FrontEndDriven.Utils;
using WinFormsValueContract;

namespace FrontEndDriven.WinForms.Adapter
{
    public class WinFormsControlAdapter : IControlAdapter
    {

        public void SetBounds(object control, int left, int top, int width, int height)
        {
            if (TryGetControl(control, out var ctrl))
                ctrl.SetBounds(left, top, width, height);
        }

        public Size GetParentClientSize(object control)
        {
            if (TryGetControl(control, out var ctrl) && ctrl.Parent != null)
                return ctrl.Parent.ClientSize;
            return Size.Empty;
        }

        public object? GetParent(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Parent : null;
        }

        public int GetLeft(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Left : 0;
        }

        public int GetTop(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Top : 0;
        }

        public int GetWidth(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Width : 0;
        }

        public int GetHeight(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Height : 0;
        }


        public void SetFont(object control, string fontFamily, float fontSize, bool bold, bool italic, bool underline)
        {
            if (!TryGetControl(control, out var ctrl)) return;

            var size = fontSize > 0 ? fontSize : ctrl.Font.Size;
            var style = FontStyle.Regular;
            if (bold) style |= FontStyle.Bold;
            if (italic) style |= FontStyle.Italic;
            if (underline) style |= FontStyle.Underline;

            ctrl.Font = new Font(fontFamily, size, style);
        }

        public void SetForeColor(object control, string htmlColor)
        {
            if (!TryGetControl(control, out var ctrl) || string.IsNullOrWhiteSpace(htmlColor))
                return;

            try
            {
                ctrl.ForeColor = ColorTranslator.FromHtml(htmlColor);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"SetForeColor 失败: control={ctrl.Name}, htmlColor={htmlColor}", ex);
            }
        }

        public void SetBackColor(object control, string htmlColor)
        {
            if (!TryGetControl(control, out var ctrl) || string.IsNullOrWhiteSpace(htmlColor))
                return;

            try
            {
                ctrl.BackColor = ColorTranslator.FromHtml(htmlColor);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"SetBackColor 失败: control={ctrl.Name}, htmlColor={htmlColor}", ex);
            }
        }


        public bool AddControl(object parent, object child)
        {
            if (!TryGetControl(parent, out var parentCtrl) || !TryGetControl(child, out var childCtrl))
                return false;

            // 防环防护：禁止将控件自身或其祖先作为子级挂载，避免 WinForms 抛循环控件引用异常
            if (ReferenceEquals(parentCtrl, childCtrl) || IsDescendantOf(childCtrl, parentCtrl))
                return false;

            // 容器语义挂载：TabControl→TabPage 经 TabPages 集合（确定性入页；选中态由 WinFormApplier.ApplyTabSelection 驱动）
            if (parentCtrl is TabControl tabControl && childCtrl is TabPage tabPage)
            {
                if (tabControl.InvokeRequired)
                {
                    UiThreadHelper.ExecuteOnUiThread(tabControl, () => AddTabPage(tabControl, tabPage));
                }
                else
                {
                    AddTabPage(tabControl, tabPage);
                }
                return true;
            }

            // M1 挂载成败契约：TabPage 必须嵌套于 TabControl——父链来自声明树，不做「猜父修正」。
            // 父非 TabControl 时无合法宿主（如 tabpage 被误挂到 Panel），记 Error 并返回 false 上抛失败信号，
            // 使「配置误嵌套」在挂载层显式暴露，而非静默 orphaning（避免挂载统计层把"调用"当"挂载事实"）。
            if (childCtrl is TabPage)
            {
                FrontEndDrivenLog.Log("WinFormsControlAdapter_Skip_TabPageNoTabControl",
                    new { parent = parentCtrl.Name, child = childCtrl.Name });
                return false;
            }

            if (parentCtrl.InvokeRequired)
            {
                UiThreadHelper.ExecuteOnUiThread(parentCtrl, () => parentCtrl.Controls.Add(childCtrl));
            }
            else
            {
                parentCtrl.Controls.Add(childCtrl);
            }
            return true;
        }

        /// <summary>把 TabPage 确定性加入 TabControl.TabPages 集合（幂等：已存在则跳过）。</summary>
        private static void AddTabPage(TabControl tabControl, TabPage tabPage)
        {
            if (!tabControl.TabPages.Contains(tabPage))
                tabControl.TabPages.Add(tabPage);
        }

        /// <summary>
        /// 物理入宿主判定（M2）：child 是否已实际进入 parent 的目标子集合。
        /// 平台适配宿主集合差异：TabControl→TabPages；其余→Controls。未解析/已释放 → false。
        /// </summary>
        public bool Contains(object parent, object child)
        {
            if (!TryGetControl(parent, out var parentCtrl) || !TryGetControl(child, out var childCtrl))
                return false;

            return parentCtrl switch
            {
                TabControl tabControl when childCtrl is TabPage tabPage
                    => tabControl.TabPages.Contains(tabPage),
                _ => parentCtrl.Controls.Contains(childCtrl)
            };
        }

        /// <summary>判断 candidate 是否为 ancestor 的祖先控件（沿 Parent 链上溯；统一到 ControlTraverser 原语）。</summary>
        private static bool IsDescendantOf(Control candidate, Control ancestor)
            => Share.ControlLookup.ControlTraverser.IsDescendantOf(ancestor, candidate);

        public void RemoveControl(object parent, object child)
        {
            if (TryGetControl(parent, out var parentCtrl) && TryGetControl(child, out var childCtrl))
                parentCtrl.Controls.Remove(childCtrl);
        }

        public void ClearControls(object parent)
        {
            if (TryGetControl(parent, out var ctrl))
                ctrl.Controls.Clear();
        }

        public void SuspendLayout(object control)
        {
            if (TryGetControl(control, out var ctrl))
                ctrl.SuspendLayout();
        }

        public void ResumeLayout(object control, bool performLayout)
        {
            if (TryGetControl(control, out var ctrl))
                ctrl.ResumeLayout(performLayout);
        }


        public string GetText(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Text : string.Empty;
        }

        public void SetText(object control, string text)
        {
            // 唯一文本写入权威：CRLF 规整 + 按需多行 + 赋值（见 ControlTextNormalizer）——消灭裸 control.Text=value 的换行/多行缺陷。
            if (TryGetControl(control, out var ctrl))
                ControlTextNormalizer.WriteText(ctrl, text);
        }

        public bool GetEnabled(object control)
        {
            return TryGetControl(control, out var ctrl) && ctrl.Enabled;
        }

        public void SetEnabled(object control, bool enabled)
        {
            if (TryGetControl(control, out var ctrl))
                ctrl.Enabled = enabled;
        }

        public bool GetVisible(object control)
        {
            return TryGetControl(control, out var ctrl) && ctrl.Visible;
        }

        public void SetVisible(object control, bool visible)
        {
            if (TryGetControl(control, out var ctrl))
                ctrl.Visible = visible;
        }

        public string? GetName(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.Name : null;
        }


        public int GetSplitterDistance(object splitContainer)
        {
            if (splitContainer is SplitContainer sc && !sc.IsDisposed)
                return sc.SplitterDistance;
            return 0;
        }

        public void SetSplitterDistance(object splitContainer, int distance)
        {
            if (splitContainer is SplitContainer sc && !sc.IsDisposed)
                sc.SplitterDistance = distance;
        }

        public Orientation GetOrientation(object splitContainer)
        {
            if (splitContainer is SplitContainer sc && !sc.IsDisposed)
                return sc.Orientation;
            return Orientation.Vertical;
        }

        public object? GetSplitterPanel(object splitContainer, string panelSide)
        {
            if (splitContainer is SplitContainer sc && !sc.IsDisposed)
            {
                return string.Equals(panelSide, UIProtocol.SplitterPanel1, StringComparison.OrdinalIgnoreCase)
                    ? sc.Panel1
                    : string.Equals(panelSide, UIProtocol.SplitterPanel2, StringComparison.OrdinalIgnoreCase)
                        ? sc.Panel2
                        : null;
            }
            return null;
        }

        private readonly Dictionary<object, SplitterEventHandler> _splitterHandlerCache = new();

        public Action SubscribeSplitterMoved(object splitContainer, EventHandler handler)
        {
            if (splitContainer is SplitContainer sc && !sc.IsDisposed && handler != null)
            {
                if (_splitterHandlerCache.TryGetValue(splitContainer, out var existing))
                {
                    sc.SplitterMoved -= existing;
                    _splitterHandlerCache.Remove(splitContainer);
                }

                SplitterEventHandler splitterHandler = (sender, e) => handler(sender, e);
                sc.SplitterMoved += splitterHandler;

                _splitterHandlerCache[splitContainer] = splitterHandler;
                return () =>
                {
                    sc.SplitterMoved -= splitterHandler;
                    _splitterHandlerCache.Remove(splitContainer);
                };
            }
            return () => { };
        }



        public void SetBorderStyle(object control, int borderWidth, string borderColor)
        {
            if (!TryGetControl(control, out var ctrl))
                return;

            if (ctrl is TextBox textBox)
            {
                textBox.BorderStyle = borderWidth > 0 ? BorderStyle.FixedSingle : BorderStyle.None;
            }
            else if (ctrl is Panel panel)
            {
                panel.BorderStyle = borderWidth > 0 ? BorderStyle.FixedSingle : BorderStyle.None;
            }
            else if (ctrl is ButtonBase buttonBase)
            {
                buttonBase.FlatStyle = borderWidth > 0 ? FlatStyle.Standard : FlatStyle.Flat;
                // E1 修复：Flat 分支内 borderWidth 必 ≤ 0，原「borderWidth > 0 ? borderWidth : 0」恒 0（死三元）——
                // 显式置零清除残留边框，删除不可达条件
                if (buttonBase.FlatStyle == FlatStyle.Flat && ctrl is Button btn)
                {
                    btn.FlatAppearance.BorderSize = 0;
                }
            }
            else if (ctrl is Label label)
            {
                label.BorderStyle = borderWidth > 0 ? BorderStyle.FixedSingle : BorderStyle.None;
            }
            else if (ctrl is ListBox listBox)
            {
                listBox.BorderStyle = borderWidth > 0 ? BorderStyle.FixedSingle : BorderStyle.None;
            }
        }


        public IEnumerable GetControls(object parent)
        {
            if (TryGetControl(parent, out var ctrl))
                return ctrl.Controls;
            return Array.Empty<object>();
        }

        public object? FindForm(object control)
        {
            return TryGetControl(control, out var ctrl) ? ctrl.FindForm() : null;
        }

        public bool IsDisposed(object control)
        {
            return control is Control c && c.IsDisposed;
        }

        public bool GetInvokeRequired(object control)
        {
            return control is Control c && c.InvokeRequired;
        }

        private static bool TryGetControl(object obj, out Control? control)
        {
            if (obj is Control c && !c.IsDisposed)
            {
                control = c;
                return true;
            }

            control = null;
            return false;
        }
    }
}
