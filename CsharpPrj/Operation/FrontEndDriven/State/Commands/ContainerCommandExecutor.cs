using System;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.State.Commands;

public sealed class ContainerCommandExecutor
{
    // 2026-08-06 重构（E2/C1）：ContainerProjector 已删除（RenderAsync 无调用方且恒真统计），
    // Load 一律经 StateCenter.Scopes.LoadAsync（ScopeLifecycleAdapter 状态管线），无投影器依赖。

    public async Task<bool> LoadToScopeAsync(string scope, string configPath)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("作用域不能为空", nameof(scope));
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException("配置路径不能为空", nameof(configPath));

        // 2026-08-05 StateCenter 收敛：Load 一律经 StateCenter.Scopes.LoadAsync（事务化 + R6 幂等短路归 StateCenter）。
        // 旧 ContainerStateStore 回填已删除——StateChanged 无消费者（状态查询统一经 StateCenter.Scopes.GetState/StateKey.Container），
        // 避免「StateCenter 与 ContainerStateStore 双写」状态事实分裂。
        var center = FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Resolve<Share.StateManagement.IStateCenter>()
            ?? throw new InvalidOperationException(
                "IStateCenter 未注册到 AppServiceProvider（StateCenterFactory 装配缺失）。请检查 StateServiceCollection.RegisterAll 已创建 StateCenter。");

        var result = await center.Scopes.LoadAsync(scope, configPath).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            FrontEndDrivenLog.Log("ContainerCommandExecutor_Load_NotSucceeded",
                new { scope, configPath, error = result.Error, nodeCount = result.NodeCount, configVersion = result.ConfigVersion });
            return false;
        }

        return true;
    }
}