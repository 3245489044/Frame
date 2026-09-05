
using CommandFramework.Abstractions;
using BP = Modules.Pipeline;

namespace CommandFramework.Extensions;

public static class RuntimeDependencyExtensions
{
    public static void PreserveRuntimeDependencies<TContext>(
        this TContext context,
        Action<TContext>? resetAction = null)
        where TContext : class, BP.IPipelineContext, IRuntimeDependencyAware
    {
        context.SaveRuntimeDependencies();

        if (resetAction != null)
        {
            resetAction(context);
        }
        else
        {
            context.Reset();
        }

        context.RestoreRuntimeDependencies();
    }
}
