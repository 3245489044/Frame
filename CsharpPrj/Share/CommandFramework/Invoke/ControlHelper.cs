
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace CommandFramework.Invoke;

public static class ControlHelper
{
    public static object? FindControl(object form, string controlName, IControlFinder finder, ILogger? logger = null)
    {
        try
        {
            // D-3（8.6）：优先在目标窗体树内递归查找——消除跨 form/跨 scope 同名字面量经全局
            // FindByName（ControlPoolSource 名单查按「最后注册者」）假命中的风险；
            // form 未知/非窗体或宿主内未命中时回退全局查找（保持原行为）。
            if (form is System.Windows.Forms.Control hostControl)
            {
                var inHost = finder.FindRecursive(hostControl, controlName);
                if (inHost != null)
                    return inHost;
            }

            return finder.FindByName<Control>(controlName);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("ControlHelper_FindControlFailed: controlName={ControlName}, error={Error}", controlName, ex.Message);
            return null;
        }
    }

    public static void SetControlText(object control, string? value, TConfig.Controls.IControlValueAccessor writer, ILogger? logger = null)
    {
        try
        {
            writer.SetValue(control, value);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("ControlHelper_SetControlTextFailed: error={Error}", ex.Message);
        }
    }
}
