namespace Share.StateManagement
{
    /// <summary>
    /// 组合（RenderPipelineComposition/EcsGate）生命周期——应用级唯一权威（治 L1）。
    /// 契约不含 UI 类型：composition/gate 由 Provider 构造时持有（R3）。
    /// </summary>
    public interface ICompositionLifecycle
    {
        /// <summary>StateCenter 在 scope 销毁时调用：只销毁该 scope 实体，绝不 Dispose composition。</summary>
        void ScopeDestroyed(string scope);

        /// <summary>进程退出唯一释放点：唯一允许调用 composition.Dispose() 的路径。</summary>
        void AppShutdown();
    }
}
