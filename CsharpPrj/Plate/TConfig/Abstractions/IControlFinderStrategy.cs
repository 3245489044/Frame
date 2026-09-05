
namespace TConfig;

public interface IControlFinderStrategy
{
    IEnumerable<object> GetChildControls(object parent);

    bool CanHandle(object target);
}

public interface IControlTreeWalker<TNode>
{
    void Walk(TNode root, Action<TNode> action);
}
