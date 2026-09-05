
namespace TConfig.Controls;

public interface IControlNode
{
    string Name { get; }

    object? NativeControl { get; }

    IControlNode? Parent { get; }

    IReadOnlyList<IControlNode> Children { get; }

    T? As<T>() where T : class;
}
