
namespace Modules.Transformers
{
    public class DelegateTransformer : ITransformer<object[], object>
    {
        private readonly Delegate _delegate;

        public string Name { get; }

        public DelegateTransformer(string name, Delegate @delegate)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _delegate = @delegate ?? throw new ArgumentNullException(nameof(@delegate));
        }

        public object Transform(object[] input)
            => _delegate.DynamicInvoke(input);
    }

    public class AsyncDelegateTransformer : IAsyncTransformer<object[], object>
    {
        private readonly Delegate _delegate;

        public string Name { get; }

        public AsyncDelegateTransformer(string name, Delegate @delegate)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _delegate = @delegate ?? throw new ArgumentNullException(nameof(@delegate));
        }

        public async Task<object> TransformAsync(object[] input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _delegate.DynamicInvoke(input);
            if (result is Task task)
            {
                await task;
                return task.GetType().GetProperty("Result")?.GetValue(task);
            }
            return result;
        }
    }
}
