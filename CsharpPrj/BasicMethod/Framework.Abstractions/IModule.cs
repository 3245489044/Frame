
namespace Modules.Framework.Abstractions
{
    public enum ModuleState
    {
        Uninitialized,
        Initializing,
        Initialized,
        ShuttingDown,
        Shutdown
    }

    public interface IModule
    {
        string Name { get; }

        ModuleState State { get; }

        void Initialize();

        void Shutdown();
    }

    public interface ILifecycleManager : IDisposable
    {
        ModuleState State { get; }

        void RegisterModule(IModule module);

        void RegisterModules(IEnumerable<IModule> modules);

        void Initialize();

        void Shutdown();

        event EventHandler? Initialized;

        event EventHandler? ShutdownCompleted;
    }
}
