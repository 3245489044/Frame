
namespace CommandFramework.Abstractions;

public interface IRuntimeDependencyAware
{
    void SaveRuntimeDependencies();

    void RestoreRuntimeDependencies();
}
