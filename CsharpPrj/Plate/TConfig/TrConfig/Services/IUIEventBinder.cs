
namespace TConfig;

public interface IUIEventBinder : IDisposable
{
    void BindAll();

    void UnbindAll();
}
