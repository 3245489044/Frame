using System;

namespace Modules.Threading
{
    /// <summary>
    /// 内联派发器：在调用（写）线程同步直跑 —— 现状默认（零回归）。
    /// 用于「未接线程模型前不改变行为」的默认装配；接线程模型（Serial/Main/Route）后替换之。
    /// </summary>
    public sealed class InlineDispatcher : IThreadDispatcher
    {
        public static readonly InlineDispatcher Instance = new();

        public void Post(Action delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            delivery();
        }
    }
}
