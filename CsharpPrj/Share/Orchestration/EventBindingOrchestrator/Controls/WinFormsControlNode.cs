
using TConfig.Controls;
using Modules.EventFramework.Utils;
using Share.EventBindingOrchestrator.Parameters;

namespace Share.EventBindingOrchestrator.Adapters;

public class WinFormsControlNode : IControlNode
{
    private readonly object _control;
    private readonly Lazy<IReadOnlyList<IControlNode>> _children;

    public WinFormsControlNode(object control)
    {
        _control = PortGuard.Required(control, nameof(control));
        _children = new Lazy<IReadOnlyList<IControlNode>>(InitializeChildren);
    }

    public string Name
    {
        get
        {
            var name = EventBindingHelper.GetControlName(_control);
            return !string.IsNullOrEmpty(name) ? name : _control.GetType().Name;
        }
    }

    public object? NativeControl => _control;

    public IControlNode? Parent
    {
        get
        {
            try
            {
                var parentProp = _control.GetType().GetProperty("Parent");
                var parent = parentProp?.GetValue(_control);
                return parent != null ? new WinFormsControlNode(parent) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public IReadOnlyList<IControlNode> Children => _children.Value;

    public T? As<T>() where T : class => _control as T;

    private IReadOnlyList<IControlNode> InitializeChildren()
    {
        try
        {
            var children = new List<IControlNode>();
            foreach (var child in ReflectionChildControlProvider.GetChildControls(_control))
            {
                children.Add(new WinFormsControlNode(child));
            }
            return children;
        }
        catch
        {
            return Array.Empty<IControlNode>();
        }
    }
}
