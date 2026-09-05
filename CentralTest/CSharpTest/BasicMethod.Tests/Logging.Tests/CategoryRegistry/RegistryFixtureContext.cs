
using Common.Diagnostics;

namespace Logging.Tests
{
    public class RegistryFixtureContext
    {
        public CategoryNode? LastRegisteredNode { get; set; }

        public List<CategoryNode> RegisteredNodes { get; } = new();

        public List<(string path, string description)> RegisteredEntries { get; } = new();
    }
}
