
using TConfig;

namespace Share.Registry
{
    public static class RegistryCoordinator
    {
        public static ITrConfigRegistry Registry
        {
            get => Modules.Registry.UnifiedRegistryCenter.Instance.Functions;
        }

        public static bool IsInitialized => Modules.Registry.UnifiedRegistryCenter.Instance.IsInitialized;

        public static void Initialize(ITrConfigRegistry registry)
        {
        }

        public static void Reset()
        {
        }
    }
}
