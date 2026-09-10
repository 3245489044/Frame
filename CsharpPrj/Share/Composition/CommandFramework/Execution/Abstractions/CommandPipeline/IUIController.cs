
namespace CommandFramework.Abstractions;

public interface IUIController
{
    void ShowForm(string windowName);

    void CloseForm(string windowName);

    void Shutdown();

    List<string> GetRegisteredFormNames();

    Task LoadFormConfigAsync(string configPath, string windowName);
}
