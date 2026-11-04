
using System.Threading;

namespace UniversalWorkflowFramework.Core
{
    public class ContextManager<T> where T : class
    {
        private sealed class Scope
        {
            public Scope(T? value, Scope? previous)
            {
                Value = value;
                Previous = previous;
            }

            public T? Value { get; }

            public Scope? Previous { get; }
        }

        // 栈式存储：Enter/Set 压栈，Exit/Clear 弹栈，嵌套时自动恢复上一层（原实现直接覆盖，内层退出会丢外层值）。
        private readonly AsyncLocal<Scope?> _context = new();

        public T? Current => _context.Value?.Value;

        public bool IsSet => _context.Value?.Value != null;

        public void Set(T value)
        {
            _context.Value = new Scope(value ?? throw new ArgumentNullException(nameof(value)), _context.Value);
        }

        public void Clear()
        {
            _context.Value = _context.Value?.Previous;
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
                _context.Value = new Scope(value, previous);
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
                _context.Value = new Scope(value, previous);
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
                _context.Value = new Scope(value, previous);
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
                _context.Value = new Scope(value, previous);
                return await asyncFunc();
            }
            finally
            {
                _context.Value = previous;
            }
        }
    }
}
