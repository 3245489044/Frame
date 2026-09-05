
#nullable enable

using System.Runtime.Versioning;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.WinForms.Utils
{
    [SupportedOSPlatform("windows")]
    public static class UILogger
    {
        public static void InvokeOnUI(Control? control, Action action)
        {
            if (control == null || control.IsDisposed)
            {
                action();
                return;
            }

            if (control.InvokeRequired)
            {
                control.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        public static void LogMessage(TextBox? textBox, string message)
        {
            FrontEndDrivenLog.Log("UILogger_LogMessage", new { message });

            if (textBox != null && !textBox.IsDisposed)
            {
                AppendTextSafe(textBox, message);
            }
        }

        public static void AppendTextSafe(TextBox? textBox, string text)
        {
            if (textBox == null || textBox.IsDisposed)
                return;

            InvokeOnUI(textBox, () =>
            {
                textBox.AppendText(text + Environment.NewLine);
            });
        }
    }
}
