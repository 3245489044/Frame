#nullable enable

using System.Collections.Generic;

namespace UniversalWorkflowFramework.Core
{
    public class ModuleProjectUnregistrationResult
    {
        public bool Success { get; set; }
        public string Address { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ResourceUnregistrationResult
    {
        public bool Success { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> RemovedResources { get; set; } = new();
        public List<string> RemovedReferences { get; set; } = new();
    }
}