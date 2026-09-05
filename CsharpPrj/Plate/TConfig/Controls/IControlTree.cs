
namespace TConfig.Controls;

public interface IControlTree
{
    IControlNode? Root { get; }

    IControlNode? Find(string name);

    IEnumerable<IControlNode> FindAll(Func<IControlNode, bool> predicate);

    void Walk(Action<IControlNode> action);
}
