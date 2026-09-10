
namespace Modules.StateManagement
{
    public static class VirtualStoreExtensions
    {
        public static string? GetValueOrDefault(this IVirtualStore store, string controlName, string? defaultValue = null)
        {
            var value = store.GetValue(controlName);
            return value ?? defaultValue;
        }

        public static bool TrySetValue(this IVirtualStore store, string controlName, string? value)
        {
            if (store.HasControl(controlName))
                return false;

            store.SetValue(controlName, value);
            return true;
        }

        public static string? PopValue(this IVirtualStore store, string controlName)
        {
            var value = store.GetValue(controlName);
            store.RemoveControl(controlName);
            return value;
        }

        public static bool HasControlPrefix(this IVirtualStore store, string prefix)
        {
            return store.GetAllControlNames().Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static Dictionary<string, string?> GetByPrefix(this IVirtualStore store, string prefix)
        {
            return store.GetAllValues()
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
