
namespace Common.PlaceholderFramework.TreeTraversal;

public interface ITreeNode<T> where T : ITreeNode<T>
{
    IEnumerable<T> GetChildren();
}
