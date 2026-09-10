
using System.Collections.Concurrent;

namespace Share.EventBindingOrchestrator.Adapters;

public class WinFormsThreadDispatcher
{
    public static readonly WinFormsThreadDispatcher Default = new();

    private readonly bool _enabled;

    private static readonly ConcurrentDictionary<object, bool> _dispatchingControls = new();

    public WinFormsThreadDispatcher(bool enabled = true)
    {
        _enabled = enabled;
    }

    public bool IsInvokeRequired(object control)
    {
        if (!_enabled || control == null)
            return false;

        try
        {
            var invokeRequiredProp = control.GetType().GetProperty("InvokeRequired");
            return invokeRequiredProp != null && (bool)(invokeRequiredProp.GetValue(control) ?? false);
        }
        catch
        {
            return false;
        }
    }

    public bool TryBeginInvoke(object control, Action action)
    {
        if (control == null || action == null)
            return false;

        if (!IsInvokeRequired(control))
            return false;

        if (!_dispatchingControls.TryAdd(control, true))
            return true;

        try
        {
            var beginInvokeMethod = control.GetType().GetMethod("BeginInvoke", new[] { typeof(Delegate) });
            if (beginInvokeMethod == null)
                return false;

            beginInvokeMethod.Invoke(control, new object[]
            {
                new Action(() =>
                {
                    try
                    {
                        action();
                    }
                    finally
                    {
                        _dispatchingControls.TryRemove(control, out _);
                    }
                })
            });
            return true;
        }
        catch (Exception ex)
        {
            _dispatchingControls.TryRemove(control, out _);
            try
            {
                action();
                return true;
            }
            catch (Exception innerEx)
            {
                Share.EventBindingOrchestrator.Diagnostics.EventBindingDiagnosticContext.Record("thread.dispatch.failed", new { error = ex.Message, fallbackError = innerEx.Message });
                throw;
            }
        }
    }

    public void BeginInvoke(object control, Action action)
    {
        if (control == null || action == null)
            return;

        try
        {
            var beginInvokeMethod = control.GetType().GetMethod("BeginInvoke", new[] { typeof(Delegate) });
            beginInvokeMethod?.Invoke(control, new object[] { action });
        }
        catch
        {
            action();
        }
    }

    public void Invoke(object control, Action action)
    {
        if (control == null || action == null)
            return;

        try
        {
            var invokeMethod = control.GetType().GetMethod("Invoke", new[] { typeof(Delegate) });
            invokeMethod?.Invoke(control, new object[] { action });
        }
        catch
        {
            action();
        }
    }

    public void EnsureOnUIThread(object control, Action action)
    {
        if (IsInvokeRequired(control))
        {
            BeginInvoke(control, action);
        }
        else
        {
            action();
        }
    }
}
