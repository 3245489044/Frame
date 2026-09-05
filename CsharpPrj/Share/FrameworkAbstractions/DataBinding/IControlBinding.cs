#nullable enable

namespace FrameworkAbstractions.DataBinding
{
    public interface IControlBinding
    {
        void BindControl(object control, string dataSourceKey, string propertyName);

        void UnbindControl(object control);

        void ClearAll();
    }
}
