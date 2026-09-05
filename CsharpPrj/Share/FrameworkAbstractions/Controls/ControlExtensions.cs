#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace FrameworkAbstractions.Controls
{
    public static class ControlExtensions
    {
        public static void EnableDoubleBuffering(this Control control)
        {
            typeof(Control).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null,
                control,
                new object[] { true });
        }

        public static void SuspendAllLayout(this Control control)
        {
            control.SuspendLayout();
            foreach (Control child in control.Controls)
            {
                child.SuspendAllLayout();
            }
        }

        public static void ResumeAllLayout(this Control control, bool performLayout = true)
        {
            foreach (Control child in control.Controls)
            {
                child.ResumeAllLayout(performLayout);
            }
            control.ResumeLayout(performLayout);
        }

        public static void SetBoundsRelative(this Control control, Rectangle relativeBounds)
        {
            if (control.Parent != null)
            {
                control.Location = new Point(relativeBounds.X, relativeBounds.Y);
                control.Size = new Size(relativeBounds.Width, relativeBounds.Height);
            }
        }
    }
}
