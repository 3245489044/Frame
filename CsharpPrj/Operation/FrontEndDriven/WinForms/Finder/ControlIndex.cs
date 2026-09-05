
#nullable enable

using FrameworkAbstractions.Tree;

namespace FrontEndDriven.Finder
{
    public class ControlIndex : TreeIndexWrapper<Control, string>
    {
        public ControlIndex() : base(StringComparer.OrdinalIgnoreCase) { }

        public void Rebuild(Control root)
        {
            ArgumentNullException.ThrowIfNull(root);

            base.Rebuild(
                root,
                getChildren: c => c.Controls.Cast<Control>(),
                keyExtractor: c => string.IsNullOrEmpty(c.Name) ? null : c.Name);
        }

        public void Register(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);

            if (!string.IsNullOrEmpty(control.Name))
                RegisterNode(control, control.Name);
        }

        public void Unregister(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);

            if (!string.IsNullOrEmpty(control.Name))
                UnregisterNode(control.Name);
        }

        public void UnregisterByName(string name)
        {
            if (!string.IsNullOrEmpty(name))
                UnregisterNode(name);
        }

        public Control? FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Find(name);
        }

        public T? FindByName<T>(string name) where T : Control
        {
            return FindByName(name) as T;
        }

        public new bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && base.Contains(name);
        }

    }
}
