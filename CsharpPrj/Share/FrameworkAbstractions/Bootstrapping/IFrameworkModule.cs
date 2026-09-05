
using Modules.Framework.Abstractions;

namespace FrameworkAbstractions.Bootstrapping
{
    public interface IFrameworkModule : IModule
    {
        bool CanRun(IBootstrapperContext context);
    }
}
