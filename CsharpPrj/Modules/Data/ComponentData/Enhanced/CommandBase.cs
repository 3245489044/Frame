namespace Modules.ComponentData.Enhanced
{
    public abstract class CommandBase<TResult>
    {
        private readonly TaskCompletionSource<TResult> _tcs = new();
        public Task<TResult> Task => _tcs.Task;
        protected void SetResult(TResult result) => _tcs.TrySetResult(result);
        public void SetException(Exception ex) => _tcs.TrySetException(ex);
        public abstract void Execute();
    }
}
