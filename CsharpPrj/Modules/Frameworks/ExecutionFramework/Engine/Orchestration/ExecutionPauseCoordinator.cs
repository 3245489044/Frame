namespace ExecutionFramework.Engine;

internal static class ExecutionPauseCoordinator
{
    internal const string PauseEventKey = "__PauseEvent";

    public static async Task CheckPauseAsync(TConfig.IExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(PauseEventKey, out var raw)
            && raw is System.Threading.ManualResetEventSlim pauseEvent
            && !pauseEvent.IsSet)
        {
            await Task.Run(() => pauseEvent.Wait(cancellationToken), cancellationToken);
        }
    }
}