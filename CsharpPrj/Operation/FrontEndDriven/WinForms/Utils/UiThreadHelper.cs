
#nullable enable

using System.Runtime.Versioning;
using System.Windows.Forms;
using FrontEndDriven.Finder;
using FrontEndDriven.Shared.Utils;

namespace FrontEndDriven.Utils
{
    [SupportedOSPlatform("windows")]
    public static class UiThreadHelper
    {
        /// <summary>
        /// 在 UI 线程同步执行委托（任一线程）。
        /// 统一委托 <see cref="DispatchIfNeeded{T}"/>——与 EL-4 修复语义一致：不再使用 control.Invoke（同步阻塞死锁），
        /// 改用 BeginInvoke + TCS + 5s 超时；保持同步返回语义，调用方可安全地随后读取控件状态。
        /// 核心实现收敛到 <see cref="UiDispatcherCore"/>（与 WinFormsUIDispatcher.Send 同一权威，消除缺陷漂移）。
        /// </summary>
        public static void ExecuteOnUiThread(Control control, Action action)
        {
            DispatchIfNeeded(control, () => { action(); return true; });
        }

        /// <summary>在 UI 线程同步执行并返回结果（任一线程）；超时抛 <see cref="TimeoutException"/>。</summary>
        public static T ExecuteOnUiThread<T>(Control control, Func<T> func)
        {
            return DispatchIfNeeded(control, func);
        }

        public static TResult DispatchIfNeeded<TResult>(object target, Func<TResult> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            return UiDispatcherCore.DispatchIfNeeded(target as Control, func);
        }

        /// <summary>
        /// 异步在 UI 线程执行并返回结果（任一线程），不阻塞调用线程。
        /// 与同步版 <see cref="DispatchIfNeeded{TResult}"/> 互补：async 调用链内应使用本方法，避免同步阻塞线程池线程。
        /// </summary>
        public static Task<TResult> DispatchIfNeededAsync<TResult>(object target, Func<TResult> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            return UiDispatcherCore.DispatchIfNeededAsync(target as Control, func);
        }

        /// <summary>
        /// E5 修复：与 <see cref="DispatchIfNeeded{TResult}"/> 统一为同步语义（BeginInvoke + TCS + 5s 超时），
        /// 消除「同名操作一个阻塞一个 fire-and-forget」的分裂（原 void 版无超时、UI 线程异常被吞）。
        /// </summary>
        public static void DispatchIfNeeded(object target, Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            UiDispatcherCore.DispatchIfNeeded(target as Control, action);
        }

        public static Form? GetTargetForm()
        {
            Form? fallback = null;
            ControlFinder.ForEachOpenFormSafe(form =>
            {
                if (form.Visible)
                {
                    fallback = form;
                    return true;
                }
                if (fallback == null)
                    fallback = form;
                return false;
            });
            return fallback;
        }
    }
}
