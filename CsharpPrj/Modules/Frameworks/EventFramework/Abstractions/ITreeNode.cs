
namespace Modules.EventFramework.Abstractions;

public interface ITreeNode
{
    bool HasChildren { get; }

    IEnumerable<ITreeNode> GetChildren();

    ITreeNode? FindChild(string name);
}
