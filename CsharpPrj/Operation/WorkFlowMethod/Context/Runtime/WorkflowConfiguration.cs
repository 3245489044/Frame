#region USINGS
using System.Collections.Generic;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Runtime
{
    public class WorkflowConfiguration
    {
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 300;
        public bool EnableLogging { get; set; } = true;
        public bool EnablePersistence { get; set; } = false;
        public string StorageConnectionString { get; set; }
        public Dictionary<string, object> DefaultInputs { get; set; } = new();
    }
}
