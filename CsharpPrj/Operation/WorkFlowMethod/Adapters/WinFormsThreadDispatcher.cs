
namespace WorkFlowMethod.Adapters;

public class WinFormsThreadDispatcher
{
    public static readonly WinFormsThreadDispatcher Default = new();

    private readonly bool _enabled;

    private readonly AsyncLocal<bool> _isDispatching = new();

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

        if (_isDispatching.Value)
            return true;

        try
        {
            var beginInvokeMethod = control.GetType().GetMethod("BeginInvoke", new[] { typeof(Delegate) });
            if (beginInvokeMethod == null)
                return false;

            _isDispatching.Value = true;
            try
            {
                beginInvokeMethod.Invoke(control, new object[]
                {
                    new Action(() =>
                    {
                        _isDispatching.Value = false;
                        action();
                    })
                });
            }
            catch
            {
                _isDispatching.Value = false;
                throw;
            }
            return true;
        }
        catch
        {
            action();
            return true;
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
