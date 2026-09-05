
namespace TConfig.Declarative
{
    public interface IDeclaration
    {
        string Name { get; }

        string Description { get; }

        string DeclarationType { get; }
    }
}
