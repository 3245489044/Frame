namespace Share.StateManagement
{
    public enum ContainerMountState { Registered, Mounted, Orphaned }

    public sealed record ContainerLifecycleInfo(
        string Scope,
        string HostKey,
        ContainerMountState MountState,
        string BindingSource,
        DateTime UpdatedAtUtc);

    /// <summary>
    /// 容器生命周期：唯一寻址（scope 优先 + hostKey 反查兜底，治 L5）+ 挂载状态唯一写入点（R5）。
    /// </summary>
    public interface IContainerLifecycle
    {
        /// <summary>注册 scope↔hostKey 绑定（幂等；重复注册更新来源标记）。</summary>
        void Register(string scope, string hostKey, string source = "code-registered");

        void Unregister(string scope);

        /// <summary>唯一寻址：scope 优先，hostKey 反查兜底（治 L5）。</summary>
        string? ResolveHostKey(string scopeOrHostKey);

        /// <summary>hostKey → scope 反查。</summary>
        string? ResolveScope(string hostKey);

        bool IsBound(string scope);

        /// <summary>挂载状态唯一写入点（R5）：由 Provider 的宿主注入成功处调用。</summary>
        void MarkMounted(string scopeOrHostKey, bool mounted);

        ContainerMountState GetMountState(string scopeOrHostKey);
    }
}
