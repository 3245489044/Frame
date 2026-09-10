
using System.Threading;

namespace UniversalWorkflowFramework.Core
{
    public class ContextManager<T> where T : class
    {
        private readonly AsyncLocal<T?> _context = new();

        public T? Current => _context.Value;

        public bool IsSet => _context.Value != null;

        public void Set(T value)
        {
            _context.Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Clear()
        {
            _context.Value = null;
        }

        public void Enter(T value)
        {
            Set(value);
        }

        public void Exit()
        {
            Clear();
        }

        public void Execute(T value, Action action)
        {
            var previous = _context.Value;
            try
            {
                _context.Value = value;
                action();
            }
            finally
            {
                _context.Value = previous;
            }
        }

        public TResult Execute<TResult>(T value, Func<TResult> func)
        {
            var previous = _context.Value;
            try
            {
                _context.Value = value;
                return func();
            }
            finally
            {
                _context.Value = previous;
            }
        }

        public async Task ExecuteAsync(T value, Func<Task> asyncAction)
        {
            var previous = _context.Value;
            try
            {
                _context.Value = value;
                await asyncAction();
            }
            finally
            {
                _context.Value = previous;
            }
        }

        public async Task<TResult> ExecuteAsync<TResult>(T value, Func<Task<TResult>> asyncFunc)
        {
            var previous = _context.Value;
            try
            {
                _context.Value = value;
                return await asyncFunc();
            }
            finally
            {
                _context.Value = previous;
            }
        }
    }
}
