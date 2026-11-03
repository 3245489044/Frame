using Share.TaskStatusPool.TaskPool;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Tasks.Orchestration
{
    /// <summary>
    /// task 生命周期端口的内存实现（产品侧最小实现）：记录建账入参与调用序（跃迁动词），零副作用。
    /// <para>留域理由：其端口 <see cref="ITaskLifecyclePort"/> 属 Agent 组合面契约（声明式编排入口专用），
    /// 非框架通用端口，按「机制下沉、载荷留域」口径随接口留在 Agent 侧。</para>
    /// </summary>
    internal sealed class DeclaredTaskLifecyclePort : ITaskLifecyclePort
    {
        private readonly List<string> _calls = new();

        /// <summary>建账入参（taskKey + 输入文本；未建账 → null）。</summary>
        internal (string Key, string? Input)? Created { get; private set; }

        /// <summary>调用序（跃迁动词：Create/Activate/Pause/Resume/Terminate）。</summary>
        internal IReadOnlyList<string> Calls => _calls;

        public Task CreateTaskAsync(string taskKey, string? input = null, CancellationToken ct = default)
        {
            Created = (taskKey, input);
            _calls.Add("Create");
            return Task.CompletedTask;
        }

        public Task ActivateTaskAsync(string taskKey, CancellationToken ct = default) => Record("Activate");

        public Task PauseTaskAsync(string taskKey, CancellationToken ct = default) => Record("Pause");

        public Task ResumeTaskAsync(string taskKey, CancellationToken ct = default) => Record("Resume");

        public Task TerminateTaskAsync(string taskKey, CancellationToken ct = default) => Record("Terminate");

        private Task Record(string verb)
        {
            _calls.Add(verb);
            return Task.CompletedTask;
        }
    }
}
