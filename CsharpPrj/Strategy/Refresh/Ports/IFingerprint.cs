namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 指纹单源契约（L0，通用）：把规范化后的内容映射为稳定指纹串。
    /// <para>去重/幂等判定的指纹实现处 = 1（本契约），领域侧旧的多重指纹退役并入此处。</para>
    /// </summary>
    public interface IFingerprint
    {
        /// <summary>对规范化内容取指纹（确定性，跨进程稳定）。</summary>
        string Of(string canonical);
    }
}

