namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 受管 worker（如 daemon 池的 node daemon）生命周期状态机。
    /// 转移：Starting→Running；Running↔Busy；Running|Busy→Hung；Running|Busy|Hung|Starting→Stopped（终态）。
    /// 泛化自《Cline 传输层 daemon 池可行性分析》§5 ClineDaemonState（Starting/Running/Busy/Hung/Stopped）。
    /// </summary>
    public enum PoolWorkerState
    {
        /// <summary>已登记、spawn 中，尚未就绪。</summary>
        Starting,

        /// <summary>就绪空闲，可接收请求。</summary>
        Running,

        /// <summary>正在处理请求（占用中）。</summary>
        Busy,

        /// <summary>疑似挂起（心跳/超时看门狗判失效，待重启）。</summary>
        Hung,

        /// <summary>已停止（终态）。</summary>
        Stopped,
    }
}
