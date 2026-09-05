
using BasicMethod.SortMent;
using ECS.Core;
using ExecutionCenter.Matrix.Components;

namespace ExecutionCenter.Matrix.Systems;

public class MonitorSystem : SystemBase
{
    public float CollectionIntervalSeconds { get; set; } = 5f;

    public Action<string>? LogAction { get; set; }

    private float _elapsed;

    private readonly ISortMentPipeline<MonitorContext> _pipeline;

    public class MonitorContext
    {
        public required MonitorMetrics Metrics { get; set; }

        public string? ReportText { get; set; }

        public Action<string>? LogOutput { get; set; }
    }

    public record MonitorMetrics(
        int Pending,
        int Running,
        int Succeeded,
        int Failed,
        int Dead,
        int Retrying,
        int Total);

    public const string PipelineName = "MonitorPipeline";

    public MonitorSystem()
    {
        _pipeline = new SortMentPipeline<MonitorContext>(PipelineName,
            "指标采集→格式化→输出 编排管线");

        _pipeline.Register(new CollectMetricsStage());
        _pipeline.Register(new FormatStage());
        _pipeline.Register(new OutputStage());
    }

    public ISortMentPipeline<MonitorContext> Pipeline => _pipeline;

    public override void Update(float deltaTime)
    {
        _elapsed += deltaTime;
        if (_elapsed < CollectionIntervalSeconds)
            return;

        _elapsed = 0f;

        int pending = 0, running = 0, succeeded = 0, failed = 0, dead = 0, retrying = 0;

        var entities = EntityManager.GetAllEntities();
        foreach (var entity in entities)
        {
            if (!EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var status))
                continue;

            switch (status.CurrentState)
            {
                case TaskStatusComponent.State.Pending: pending++; break;
                case TaskStatusComponent.State.Running: running++; break;
                case TaskStatusComponent.State.Succeeded: succeeded++; break;
                case TaskStatusComponent.State.Failed: failed++; break;
                case TaskStatusComponent.State.Dead: dead++; break;
                case TaskStatusComponent.State.Retrying: retrying++; break;
            }
        }

        var metrics = new MonitorMetrics(
            pending, running, succeeded, failed, dead, retrying,
            pending + running + succeeded + failed + dead + retrying);

        var logOutput = LogAction;

        var context = new MonitorContext
        {
            Metrics = metrics,
            LogOutput = logOutput
        };

        _pipeline.Execute(context);
    }

    #region 管线阶段实现

    private class CollectMetricsStage : ISortableMent<MonitorContext, MonitorContext>
    {
        public string Name => "CollectMetrics";
        public string Description => "采集阶段：指标总量校验";
        public int Order => 10;

        public MonitorContext Morph(MonitorContext context)
        {
            var m = context.Metrics;
            int sum = m.Pending + m.Running + m.Succeeded + m.Failed + m.Dead + m.Retrying;
            if (sum != m.Total)
            {
                context.Metrics = m with { Total = sum };
            }
            return context;
        }
    }

    private class FormatStage : ISortableMent<MonitorContext, MonitorContext>
    {
        public string Name => "FormatReport";
        public string Description => "格式化阶段：构建可读报告文本";
        public int Order => 20;

        public MonitorContext Morph(MonitorContext context)
        {
            var m = context.Metrics;
            context.ReportText = $"[{DateTime.Now:HH:mm:ss}] Tasks: {m.Pending}p / {m.Running}r / {m.Succeeded}✓ / {m.Failed}✗ / {m.Retrying}↻ / {m.Dead}💀 (total={m.Total})";
            return context;
        }
    }

    private class OutputStage : ISortableMent<MonitorContext, MonitorContext>
    {
        public string Name => "OutputReport";
        public string Description => "输出阶段：将格式化报告写入日志";
        public int Order => 30;

        public MonitorContext Morph(MonitorContext context)
        {
            context.LogOutput?.Invoke(context.ReportText ?? "No report text generated");
            return context;
        }
    }

    #endregion
}