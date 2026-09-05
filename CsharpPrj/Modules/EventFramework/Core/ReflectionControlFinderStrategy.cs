
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Utils;
using TConfig;

namespace Modules.EventFramework.Core;

public class ReflectionControlFinderStrategy : IControlFinderStrategy
{
    public IEnumerable<object> GetChildControls(object parent)
    {
        return ReflectionChildControlProvider.GetChildControls(parent);
    }

    public bool CanHandle(object control)
    {
        return control != null;
    }
}
