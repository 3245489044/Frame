
namespace ParameterResolver.Utilities;

public interface IThreadDispatcher
{
    bool TryBeginInvoke(object target, Action action);

    bool TryInvokeOnUIThread(object target, Action action);
}
