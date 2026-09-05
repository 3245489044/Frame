
namespace Modules.Framework.Abstractions
{
    public interface ITreeSearch<TNode>
    {
        TNode? FindByName(TNode root, string name);

        TNode? FindByType<TTarget>(TNode root) where TTarget : TNode;

        TNode? FindBy(TNode root, Func<TNode, bool> predicate);

        List<TNode> FindAllByName(TNode root, string name);

        List<TNode> FindAllByType<TTarget>(TNode root) where TTarget : TNode;

        List<TNode> FindAllBy(TNode root, Func<TNode, bool> predicate);
    }
}
