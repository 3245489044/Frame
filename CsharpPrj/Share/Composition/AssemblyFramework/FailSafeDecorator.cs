using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 观测装饰 fail-safe 骨架：宿主未就绪 → 全部 Safe 调用 no-op；观测异常就地吞并留痕；业务异常原样抛。
    /// <para>通用件：零领域类型（就绪判定与观测动作一律由派生类以属性/委托提供）。</para>
    /// </summary>
    public abstract class FailSafeDecorator
    {
        /// <summary>观测宿主是否已装配（false → 全部 Safe 调用 no-op；零回归）。</summary>
        protected abstract bool IsHostReady { get; }

        /// <summary>最近一次观测失败原因（诊断可见性）；null = 无失败。</summary>
        public string? LastObserveError { get; private set; }

        /// <summary>执行观测动作（返回观测句柄）；未就绪或异常 → <c>default</c>（不阻断主路径）。</summary>
        protected T? Safe<T>(Func<T> observe)
        {
            if (!IsHostReady) return default;

            try
            {
                return observe();
            }
            catch (Exception ex)
            {
                LastObserveError = ex.Message; // 观测不阻断主路径
                return default;
            }
        }

        /// <summary>执行观测动作；未就绪或异常 → no-op（仅留痕）。</summary>
        protected void Safe(Action observe)
        {
            if (!IsHostReady) return;

            try
            {
                observe();
            }
            catch (Exception ex)
            {
                LastObserveError = ex.Message; // 观测不阻断主路径
            }
        }
    }
}
