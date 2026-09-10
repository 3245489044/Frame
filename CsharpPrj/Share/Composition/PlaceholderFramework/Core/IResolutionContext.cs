
using Graphkey.Core;

namespace Common.PlaceholderFramework.Core
{
    public interface IResolutionContext : Graphkey.Core.IResolutionContext
    {
        new IReadOnlyList<string> PreviousOutputs { get; }

        new IReadOnlyCollection<string> InputKeys { get; }

        new IReadOnlyList<string> GetNodeOutputs(string nodeId);

        new IReadOnlyList<string> GetNodeOutputs(int index);
    }
}
