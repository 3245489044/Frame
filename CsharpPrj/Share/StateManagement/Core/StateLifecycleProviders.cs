namespace Share.StateManagement
{
    /// <summary>
    /// 生命周期 Provider 注册容器（StateCenter 装配入口）。
    /// 未注册即抛（InvalidOperationException），保证装配顺序错误在启动期暴露而非运行期静默。
    /// </summary>
    public sealed class StateLifecycleProviders
    {
        private IScopeLifecycle? _scopeLifecycle;
        private ICompositionLifecycle? _compositionLifecycle;
        private IContainerLifecycle? _containerLifecycle;

        public void RegisterScopeLifecycle(IScopeLifecycle provider)
        {
            _scopeLifecycle = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void RegisterCompositionLifecycle(ICompositionLifecycle provider)
        {
            _compositionLifecycle = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void RegisterContainerLifecycle(IContainerLifecycle provider)
        {
            _containerLifecycle = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public IScopeLifecycle GetRequiredScopeLifecycle()
            => _scopeLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：IScopeLifecycle");

        public ICompositionLifecycle GetRequiredCompositionLifecycle()
            => _compositionLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：ICompositionLifecycle");

        public IContainerLifecycle GetRequiredContainerLifecycle()
            => _containerLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：IContainerLifecycle");

        public bool IsAllRegistered => _scopeLifecycle != null && _compositionLifecycle != null && _containerLifecycle != null;
    }
}
