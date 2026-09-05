using System.Collections.Generic;
using System.Threading.Tasks;
using FrontEndDriven.Eventing;

namespace FrontEndDriven.Lifecycle;

public static class LifecycleHookExecutor
{
    public static async Task ExecuteHooksAsync(
        EventPlatform platform,
        IEnumerable<string>? hookNames,
        string scope)
    {
        if (hookNames == null)
            return;

        foreach (var hookName in hookNames)
        {
            if (string.IsNullOrWhiteSpace(hookName))
                continue;

            await platform.InvokeFunctionAsync(hookName, source: "LifecycleHook", timeoutMs: 10_000)
                .ConfigureAwait(false);
        }
    }
}