#nullable enable

namespace FrameworkAbstractions.DataBinding
{
    public interface IPropertySubscription
    {
        void SubscribeToProperty(string key, string propertyName, Action<object?> handler);
    }
}
