namespace BasicMethod.Initialization.Modules;

/// <summary>可初始化组件的生命周期状态。</summary>
public enum InitState
{
    /// <summary>已构造，未初始化。</summary>
    Created,

    /// <summary>初始化进行中。</summary>
    Initializing,

    /// <summary>装配完成，对外可用。</summary>
    Ready,

    /// <summary>初始化失败。</summary>
    Failed,

    /// <summary>已释放。</summary>
    Disposed,
}
