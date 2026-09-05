
namespace CommandFramework.Abstractions;

public interface IStateQuery
{
    List<string> GetLoadedConfigNames();

    bool HasControlValue(string controlName);

    List<string> GetRegisteredFormNames();

    List<string> GetRegisteredCommandNames();
}
