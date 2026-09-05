
using System;
using System.Collections.Generic;
using Modules.Framework;
using Modules.Framework.Abstractions;
using FrameworkAbstractions.Bootstrapping;

namespace FrameworkAbstractions.Bootstrapping
{
    public class FrameworkBootstrapper
    {
        private readonly LifecycleManager _lifecycle = new();
        private readonly List<(IFrameworkModule Module, Func<IBootstrapperContext, bool>? Condition)> _modules = new();
        private bool _initialized;

        public ModuleState State => _lifecycle.State;

        public FrameworkBootstrapper UseModule<T>() where T : IFrameworkModule, new()
        {
            var module = new T();
            _modules.Add((module, null));
            return this;
        }

        public FrameworkBootstrapper UseModule(IFrameworkModule module)
        {
            _modules.Add((module, null));
            return this;
        }

        public FrameworkBootstrapper UseConditional<T>(Func<IBootstrapperContext, bool> condition)
            where T : IFrameworkModule, new()
        {
            var module = new T();
            _modules.Add((module, condition));
            return this;
        }

        public FrameworkBootstrapper UseConditional(IFrameworkModule module, Func<IBootstrapperContext, bool> condition)
        {
            _modules.Add((module, condition));
            return this;
        }

        public void Run(IBootstrapperContext context)
        {
            if (_initialized) return;

            foreach (var (module, condition) in _modules)
            {
                if (condition != null && !condition(context))
                {
                    continue;
                }

                if (!module.CanRun(context))
                {
                    continue;
                }

                _lifecycle.RegisterModule(module);
            }

            _lifecycle.Initialize();
            _initialized = true;
        }

        public void Shutdown()
        {
            if (!_initialized) return;

            _lifecycle.Shutdown();
            _initialized = false;
        }
    }
}
