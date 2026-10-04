
using Modules.StateManagement;

namespace Share.StateManagement
{
    public static class VirtualStoreExtensions
    {
        public static bool TryGetValue(this IVirtualStore store, string controlName, out string? value)
        {
            PortGuard.Required(store, nameof(store));

            value = store.GetValue(controlName);
            return store.HasControl(controlName);
        }

        public static bool ContainsAll(this IVirtualStore store, IEnumerable<string> controlNames)
        {
            PortGuard.Required(store, nameof(store));
            PortGuard.Required(controlNames, nameof(controlNames));

            return controlNames.All(c => store.HasControl(c));
        }

        public static Dictionary<string, string?> FindByPrefix(this IVirtualStore store, string prefix)
        {
            PortGuard.Required(store, nameof(store));

            if (string.IsNullOrEmpty(prefix))
                return store.GetAllValues();

            var result = new Dictionary<string, string?>();
            foreach (var name in store.GetAllControlNames())
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result[name] = store.GetValue(name);
                }
            }
            return result;
        }

        public static IReadOnlyDictionary<string, string?> AsReadOnlyDictionary(this IVirtualStore store)
        {
            PortGuard.Required(store, nameof(store));

            if (store is VirtualStore vs)
                return vs.AsReadOnlyDictionary();

            return store.GetAllValues();
        }
    }
}
