
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class LifecycleManager : ILifecycleManager
    {
        private readonly List<IModule> _modules = new();
        private readonly object _lock = new();
        private bool _disposed;

        public ModuleState State { get; private set; } = ModuleState.Uninitialized;

        public event EventHandler? Initialized;

        public event EventHandler? ShutdownCompleted;

        public void RegisterModule(IModule module)
        {
            ArgumentNullException.ThrowIfNull(module);

            lock (_lock)
            {
                if (_modules.Any(m => m.Name == module.Name))
                    throw new InvalidOperationException($"模块 '{module.Name}' 已注册。");

                _modules.Add(module);
            }
        }

        public void RegisterModules(IEnumerable<IModule> modules)
        {
            ArgumentNullException.ThrowIfNull(modules);

            foreach (var module in modules)
            {
                RegisterModule(module);
            }
        }

        public void Initialize()
        {
            lock (_lock)
            {
                if (State != ModuleState.Uninitialized)
                    throw new InvalidOperationException($"当前状态为 {State}，无法初始化。");

                State = ModuleState.Initializing;
            }

            try
            {
                foreach (var module in _modules)
                {
                    module.Initialize();
                }

                State = ModuleState.Initialized;
                Initialized?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                State = ModuleState.Uninitialized;
                throw;
            }
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                if (State != ModuleState.Initialized)
                    return;

                State = ModuleState.ShuttingDown;
            }

            try
            {
                for (int i = _modules.Count - 1; i >= 0; i--)
                {
                    _modules[i].Shutdown();
                }

                State = ModuleState.Shutdown;
                ShutdownCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                State = ModuleState.ShuttingDown;
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (State == ModuleState.Initialized)
                {
                    Shutdown();
                }

                _modules.Clear();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
