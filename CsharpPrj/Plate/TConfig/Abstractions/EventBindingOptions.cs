
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TConfig
{
    public class EventBindingOptions
    {
        public bool Enabled { get; set; } = true;

        public int TimeoutMs { get; set; } = 5000;

        public bool EnableLogging { get; set; } = true;

        public bool ThrowOnError { get; set; } = false;

        public BindingMode Mode { get; set; } = BindingMode.Auto;

        public bool ScriptExecutionEnabled { get; set; } = false;

        public Action<string>? Logger { get; set; }


        public IEventBus? EventBus { get; set; }

        public object? SearchRoot { get; set; }

        public Func<IEventExecutionService?>? ExecutionServiceResolver { get; set; }

        public Action<string, string, object?>? OnEventResult { get; set; }

        public IControlFinderStrategy? ControlFinderStrategy { get; set; }

        public ITrConfigRegistry? Registry { get; set; }

        public Func<string, Dictionary<string, object>, Task>? ExecuteFunctionDelegate { get; set; }

        public EventBindingOptions Clone()
        {
            return (EventBindingOptions)MemberwiseClone();
        }
    }

    public enum BindingMode
    {
        Auto,
        Manual,
        Batch
    }
}
