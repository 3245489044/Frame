#nullable enable

using System.Collections.Generic;

namespace UniversalWorkflowFramework.Reference.Models
{
    public class CircularDependencyResult
    {
        public bool HasCircularDependencies { get; set; }
        public IEnumerable<CircularDependency> CircularDependencies { get; set; } = new List<CircularDependency>();
    }

    public class CircularDependency
    {
        public List<string> ResourceChain { get; set; } = new();
        public ReferenceType ReferenceType { get; set; }
    }
}
