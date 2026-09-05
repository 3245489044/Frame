using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.Diagnostics;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class HandlersTests : TestBase
    {
        [TestMethod]
        public void RunAll_LogPlaceholderHandler()
        {
            var result = RunTestGroup("LogPlaceholderHandler", builder =>
            {
                builder.Add("CanHandle_LogPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("log:user_login");
                    return handler.CanHandle(ph);
                });

                builder.Add("CanHandle_OrderPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("order:create");
                    return handler.CanHandle(ph);
                });

                builder.Add("CanHandle_DebugPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("debug:connection");
                    return handler.CanHandle(ph);
                });

                builder.Add("CanHandle_OtherPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("monitor:cpu");
                    return !handler.CanHandle(ph);
                });

                builder.Add("Handle_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("TestModule");
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("log:test");

                    handler.Handle(ph, "test-value", ctx, "batch-001");
                    return true;
                });

                builder.Add("Handle_ShouldNotThrow_WithLogPrefix", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("TestModule");
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("log:user_login");

                    handler.Handle(ph, "user123", ctx, "batch-001");
                    return true;
                });

                builder.Add("Handle_MultipleCalls_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("TestModule");
                    var handler = new LogPlaceholderHandler();

                    handler.Handle(Placeholder.Input("log:action1"), "data1", ctx, "batch-001");
                    handler.Handle(Placeholder.Input("log:action2"), "data2", ctx, "batch-001");
                    return true;
                });

                builder.Add("CanHandle_EmptyPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new LogPlaceholderHandler();
                    var ph = Placeholder.Input("");
                    return !handler.CanHandle(ph);
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_MonitorPlaceholderHandler()
        {
            var result = RunTestGroup("MonitorPlaceholderHandler", builder =>
            {
                builder.Add("CanHandle_MonitorPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new MonitorPlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("monitor:cpu_usage"));
                });

                builder.Add("CanHandle_MetricPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new MonitorPlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("metric:memory"));
                });

                builder.Add("CanHandle_AlertPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new MonitorPlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("alert:disk_full"));
                });

                builder.Add("CanHandle_OtherPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new MonitorPlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input("log:test"));
                });

                builder.Add("Handle_ShouldStoreMetricsInContext", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("MonitorModule");
                    var handler = new MonitorPlaceholderHandler();
                    var ph = Placeholder.Input("monitor:cpu");

                    handler.Handle(ph, "85.5", ctx, "batch-001");

                    var metrics = ctx.GetData<Dictionary<string, object>>("__MonitorMetrics");
                    return metrics != null
                        && metrics.ContainsKey("monitor:cpu")
                        && metrics["monitor:cpu"] as string == "85.5";
                });

                builder.Add("Handle_WithThreshold_ShouldTriggerAlert", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("AlertModule");
                    ctx.SetData(new Dictionary<string, double>
                    {
                        ["monitor:cpu"] = 80.0
                    }, "__AlertThresholds");

                    var handler = new MonitorPlaceholderHandler();
                    var ph = Placeholder.Input("monitor:cpu");

                    handler.Handle(ph, "95.0", ctx, "batch-001");

                    var alerts = ctx.GetData<List<string>>("__Alerts");
                    return alerts != null && alerts.Count == 1
                        && alerts[0].Contains("monitor:cpu");
                });

                builder.Add("Handle_WithThresholdNotExceeded_ShouldNotTriggerAlert", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("AlertModule");
                    ctx.SetData(new Dictionary<string, double>
                    {
                        ["monitor:cpu"] = 80.0
                    }, "__AlertThresholds");

                    var handler = new MonitorPlaceholderHandler();
                    var ph = Placeholder.Input("monitor:cpu");

                    handler.Handle(ph, "50.0", ctx, "batch-001");

                    var hasAlerts = ctx.TryGetData<List<string>>(out var alerts, "__Alerts");
                    return !hasAlerts || alerts == null || alerts.Count == 0;
                });

                builder.Add("Handle_MultipleMetrics_ShouldStoreAll", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("MonitorModule");
                    var handler = new MonitorPlaceholderHandler();

                    handler.Handle(Placeholder.Input("monitor:cpu"), "85.5", ctx, "batch-001");
                    handler.Handle(Placeholder.Input("monitor:memory"), "70.2", ctx, "batch-001");

                    var metrics = ctx.GetData<Dictionary<string, object>>("__MonitorMetrics");
                    return metrics != null && metrics.Count == 2;
                });

                builder.Add("CanHandle_EmptyPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new MonitorPlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input(""));
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_AuditPlaceholderHandler()
        {
            var result = RunTestGroup("AuditPlaceholderHandler", builder =>
            {
                builder.Add("CanHandle_AuditPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new AuditPlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("audit:user_login"));
                });

                builder.Add("CanHandle_TracePrefix_ShouldReturnTrue", () =>
                {
                    var handler = new AuditPlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("trace:request_flow"));
                });

                builder.Add("CanHandle_OtherPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new AuditPlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input("log:test"));
                });

                builder.Add("Handle_ShouldStoreAuditEntry", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("AuditModule");
                    var handler = new AuditPlaceholderHandler();
                    var ph = Placeholder.Input("audit:user_login");

                    handler.Handle(ph, "user123", ctx, "batch-001");

                    var trail = ctx.GetData<List<AuditEntry>>("__AuditTrail");
                    return trail != null && trail.Count == 1
                        && trail[0].Action == "audit:user_login"
                        && trail[0].Data == "user123"
                        && trail[0].SourceModule == "AuditModule"
                        && trail[0].BatchId == "batch-001";
                });

                builder.Add("Handle_MultipleCalls_ShouldAppendEntries", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("AuditModule");
                    var handler = new AuditPlaceholderHandler();

                    handler.Handle(Placeholder.Input("audit:action1"), "data1", ctx, "batch-001");
                    handler.Handle(Placeholder.Input("audit:action2"), "data2", ctx, "batch-001");

                    var trail = ctx.GetData<List<AuditEntry>>("__AuditTrail");
                    return trail != null && trail.Count == 2;
                });

                builder.Add("CanHandle_EmptyPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new AuditPlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input(""));
                });

                builder.Add("Handle_WithNullData_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("AuditModule");
                    var handler = new AuditPlaceholderHandler();
                    var ph = Placeholder.Input("audit:test");

                    handler.Handle(ph, null, ctx, "batch-001");

                    var trail = ctx.GetData<List<AuditEntry>>("__AuditTrail");
                    return trail != null && trail.Count == 1;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PerformancePlaceholderHandler()
        {
            var result = RunTestGroup("PerformancePlaceholderHandler", builder =>
            {
                builder.Add("CanHandle_PerfPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new PerformancePlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("perf:db_query"));
                });

                builder.Add("CanHandle_TimingPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new PerformancePlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("timing:api_call"));
                });

                builder.Add("CanHandle_DurationPrefix_ShouldReturnTrue", () =>
                {
                    var handler = new PerformancePlaceholderHandler();
                    return handler.CanHandle(Placeholder.Input("duration:render"));
                });

                builder.Add("CanHandle_OtherPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new PerformancePlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input("log:test"));
                });

                builder.Add("Handle_ShouldStorePerformanceRecord", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("PerfModule");
                    var handler = new PerformancePlaceholderHandler();
                    var ph = Placeholder.Input("perf:db_query");

                    handler.Handle(ph, "150.5", ctx, "batch-001");

                    var records = ctx.GetData<List<PerformanceRecord>>("__PerformanceData");
                    return records != null && records.Count == 1
                        && records[0].Key == "perf:db_query"
                        && Math.Abs(records[0].Duration - 150.5) < 0.01
                        && records[0].Module == "PerfModule";
                });

                builder.Add("Handle_ShouldUpdateStats", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("PerfModule");
                    var handler = new PerformancePlaceholderHandler();
                    var ph = Placeholder.Input("perf:api");

                    handler.Handle(ph, "100.0", ctx, "batch-001");
                    handler.Handle(ph, "200.0", ctx, "batch-001");

                    var stats = ctx.GetData<PerformanceStats>("__PerformanceStats");
                    return stats != null
                        && Math.Abs(stats.GetAverage("perf:api") - 150.0) < 0.01
                        && Math.Abs(stats.GetMax("perf:api") - 200.0) < 0.01
                        && Math.Abs(stats.GetMin("perf:api") - 100.0) < 0.01
                        && stats.GetCount("perf:api") == 2;
                });

                builder.Add("Handle_NonNumericValue_ShouldNotThrow", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("PerfModule");
                    var handler = new PerformancePlaceholderHandler();
                    var ph = Placeholder.Input("perf:test");

                    handler.Handle(ph, "not-a-number", ctx, "batch-001");
                    return true;
                });

                builder.Add("CanHandle_EmptyPrefix_ShouldReturnFalse", () =>
                {
                    var handler = new PerformancePlaceholderHandler();
                    return !handler.CanHandle(Placeholder.Input(""));
                });

                builder.Add("Handle_MultipleKeys_ShouldTrackSeparately", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("PerfModule");
                    var handler = new PerformancePlaceholderHandler();

                    handler.Handle(Placeholder.Input("perf:db"), "50.0", ctx, "batch-001");
                    handler.Handle(Placeholder.Input("perf:api"), "150.0", ctx, "batch-001");

                    var stats = ctx.GetData<PerformanceStats>("__PerformanceStats");
                    return stats != null
                        && stats.GetCount("perf:db") == 1
                        && stats.GetCount("perf:api") == 1;
                });

                builder.Add("Handle_ZeroValue_ShouldStoreCorrectly", () =>
                {
                    using var ctx = new SkeletonContext();
                    ctx.SetModuleName("PerfModule");
                    var handler = new PerformancePlaceholderHandler();
                    var ph = Placeholder.Input("perf:test");

                    handler.Handle(ph, "0.0", ctx, "batch-001");

                    var records = ctx.GetData<List<PerformanceRecord>>("__PerformanceData");
                    return records != null && records.Count == 1
                        && Math.Abs(records[0].Duration - 0.0) < 0.01;
                });
            });

            result.AssertAllPassed();
        }
    }
}
