#region USINGS
using Modules.Node.Diagnostics;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Diagnostics
{
    /// <summary>
    /// 框架诊断 → <see cref="WorkFlowMethod.Diagnostics.WorkFlowMethodLog"/> 单向投影（宿主诊断接线）：
    /// 缺口一律可见化（治 P12 静默反射猜端口 / P15 静默跳过 / P16 门缝），不静默兜底。
    /// 实例级注入，禁宿主侧进程静态可变配置。
    /// </summary>
    internal sealed class NodeFrameworkLogSink : INodeDiagnosticsSink
    {
        public static readonly NodeFrameworkLogSink Instance = new();

        private NodeFrameworkLogSink()
        {
        }

        public void Report(NodeDiagnostic diagnostic)
        {
            switch (diagnostic.Severity)
            {
                case NodeDiagnosticSeverity.Error:
                    WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Error(
                        "NodeFramework", $"{diagnostic.Code}: {diagnostic.Message}", data: diagnostic.Subject);
                    break;

                case NodeDiagnosticSeverity.Warning:
                    WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Warn(
                        "NodeFramework", $"{diagnostic.Code}: {diagnostic.Message}", diagnostic.Subject);
                    break;

                default:
                    WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Info(
                        "NodeFramework", $"{diagnostic.Code}: {diagnostic.Message}", diagnostic.Subject);
                    break;
            }
        }
    }
}
