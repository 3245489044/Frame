using System;
using System.Threading;

namespace Modules.Threading
{
    /// <summary>
    /// 主线程/UI 封送派发器：把工作回投到某一线程模型（UI/SynchronizationContext 或自定义 main-thread dispatcher）。
    /// 承载 WinFormsDispatcher(IUIDispatcher) 与 ThreadContext 的封送逻辑；已在该上下文线程则内联执行。
    /// </summary>
    public sealed class MainThreadDispatcher : IThreadDispatcher
    {
        private readonly SynchronizationContext? _context;
        private readonly Action<Action>? _custom;
        private readonly int? _affinityThreadId;

        /// <summary>以既有 SynchronizationContext（如 WindowsFormsSynchronizationContext）回投。</summary>
        public MainThreadDispatcher(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>以自定义 main-thread dispatcher（形如 WinForms 的 Post/BeginInvoke）回投；affinityThreadId=主线程 id。</summary>
        public MainThreadDispatcher(Action<Action> dispatcher, int affinityThreadId)
        {
            _custom = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _affinityThreadId = affinityThreadId;
        }

        /// <summary>是否处于该派发器所属线程（可内联）。</summary>
        public bool IsOnTargetThread
            => (_affinityThreadId.HasValue && Environment.CurrentManagedThreadId == _affinityThreadId.Value)
               || (_context != null && ReferenceEquals(SynchronizationContext.Current, _context));

        public void Post(Action delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            if (IsOnTargetThread)
            {
                delivery();
                return;
            }

            if (_custom != null)
            {
                _custom(delivery);
                return;
            }

            _context!.Post(_ => delivery(), null);
        }
    }
}
