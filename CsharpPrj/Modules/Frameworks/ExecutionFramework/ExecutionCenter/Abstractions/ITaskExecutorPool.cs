namespace ExecutionCenter.Abstractions;

public interface ITaskExecutorPool : IDisposable
{
    Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct = default);

    void Start();

    void Stop();
}
