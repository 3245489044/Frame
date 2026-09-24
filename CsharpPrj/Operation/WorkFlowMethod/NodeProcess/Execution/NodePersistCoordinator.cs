#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Materialization;
using WorkFlowMethod.Skeletons.NodeProcess.Diagnostics;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点输出单写协调器 —— 单写门唯一实现 = <see cref="NodeWriteGate"/>（G3 收编，治 P16）：
    ///   ① <b>单写者 fail-fast</b>：同一 target 一旦有写者在写，任一平行写立即抛 <see cref="NodeWriteConflictException"/>
    ///      （<see cref="InvalidOperationException"/> 派生，兼容既有捕获），不排队、不阻塞等待
    ///      （防两个实现/能力同写一 target 造成竞争）。
    ///   ② <b>串行</b>：同 target 写入经门串行化；异 target 并行。
    ///   ③ 目标以「工作流/workflowId」为主要判定键；同步路径与未来 Outbox 后台共用同一实例即保持确定性。
    /// 与库级写通道（<c>SqliteWriteChannel</c> / 按库有界写门）关系为「节点级 ⊆ 库级」，二者判据不同（治 P16 门缝）。
    /// </summary>
    public sealed class NodePersistCoordinator
    {
        private const string GateId = "nodepersist";

        private readonly NodeWriteGate _gate;

        /// <param name="boundedWait">
        /// 历史参数（有界等待上限）：单写门语义为 fail-fast（不排队），该值不再参与等待，仅为兼容既有调用签名保留。
        /// </param>
        public NodePersistCoordinator(TimeSpan? boundedWait = null)
        {
            _ = boundedWait;
            _gate = new NodeWriteGate(GateId, TimeSpan.Zero, NodeFrameworkLogSink.Instance);
        }

        public Task RunWriteAsync(string target, Func<CancellationToken, Task> body, CancellationToken ct = default)
            => RunWriteAsync(target, body, TimeSpan.Zero, ct);

        /// <param name="timeout">历史参数（有界等待上限）：同构造参数说明，不再参与等待。</param>
        public Task RunWriteAsync(

            string target,
            Func<CancellationToken, Task> body,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("写目标不能为空", nameof(target));
            ArgumentNullException.ThrowIfNull(body);

            _ = timeout;
            return _gate.RunAsync(target, () => body(ct), ct);
        }
    }
}

