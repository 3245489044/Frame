
using Common.Diagnostics;
using SkeletonFlow;
using SkeletonFlow.App;

namespace Logging.Tests
{
    public static class RegistryFixture
    {
        public static readonly ISkeleton<Unit, RegistryFixtureContext> Clear =
            new DelegateSkeleton<Unit, RegistryFixtureContext>("ClearRegistry", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                return new RegistryFixtureContext();
            });

        public static ISkeleton<Unit, RegistryFixtureContext> Register(string path, string description) =>
            new DelegateSkeleton<Unit, RegistryFixtureContext>($"Register({path})", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                var node = LogCategoryRegistry.Register(path, description);
                var ctx = new RegistryFixtureContext();
                ctx.LastRegisteredNode = node;
                ctx.RegisteredNodes.Add(node);
                ctx.RegisteredEntries.Add((path, description));
                return ctx;
            });

        public static ISkeleton<Unit, RegistryFixtureContext> RegisterRange(params (string path, string desc)[] entries) =>
            new DelegateSkeleton<Unit, RegistryFixtureContext>($"RegisterRange({entries.Length})", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                var ctx = new RegistryFixtureContext();
                foreach (var (path, desc) in entries)
                {
                    var node = LogCategoryRegistry.Register(path, desc);
                    ctx.RegisteredNodes.Add(node);
                    ctx.RegisteredEntries.Add((path, desc));
                }
                return ctx;
            });

        public static readonly ISkeleton<Unit, RegistryFixtureContext> InitDefaults =
            new DelegateSkeleton<Unit, RegistryFixtureContext>("InitDefaults", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                LogCategoryRegistry.InitializeDefaults();
                return new RegistryFixtureContext();
            });

        public static ISkeleton<Unit, RegistryFixtureContext> RegisterModule(string moduleName, params (string name, string desc)[] features) =>
            new DelegateSkeleton<Unit, RegistryFixtureContext>($"RegisterModule({moduleName})", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                var ctx = new RegistryFixtureContext();
                LogCategoryRegistry.RegisterModule(moduleName,
                    features.Select(f => (f.name, f.desc)).ToArray());
                foreach (var (name, desc) in features)
                {
                    var path = $"{moduleName}/{name}";
                    var node = LogCategoryRegistry.GetNode(path);
                    if (node != null)
                    {
                        ctx.RegisteredNodes.Add(node);
                        ctx.RegisteredEntries.Add((path, desc));
                    }
                }
                return ctx;
            });

        public static ISkeleton<Unit, RegistryFixtureContext> RegisterTree(Dictionary<string, object> treeDef) =>
            new DelegateSkeleton<Unit, RegistryFixtureContext>("RegisterTree", (_, _) =>
            {
                LogCategoryRegistry.Clear();
                RegisterTreeRecursive(treeDef, "");
                return new RegistryFixtureContext();
            });

        private static void RegisterTreeRecursive(Dictionary<string, object> node, string prefix)
        {
            foreach (var kvp in node)
            {
                var path = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}/{kvp.Key}";
                if (kvp.Value is string description)
                {
                    LogCategoryRegistry.Register(path, description);
                }
                else if (kvp.Value is Dictionary<string, object> children)
                {
                    RegisterTreeRecursive(children, path);
                }
            }
        }
    }
}
