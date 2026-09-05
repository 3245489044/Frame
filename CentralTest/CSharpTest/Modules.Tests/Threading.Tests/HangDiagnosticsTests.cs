using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class HangDiagnosticsTests
{
    [TestMethod]
    public void RingBuffer_Retains_LastCapacity_Newest()
    {
        var ring = new RingBuffer<int>(3);
        ring.AddRange(new[] { 1, 2, 3, 4, 5 });

        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, ring.Snapshot());
        Assert.AreEqual(3, ring.Count);

        ring.Clear();
        Assert.AreEqual(0, ring.Count);
    }

    private sealed class StubSource : IHangEvidenceSource
    {
        public string Name => "db";
        public object? Capture() => new { busy = true, writerId = 42 };
    }

    private sealed class ThrowingSource : IHangEvidenceSource
    {
        public string Name => "fragile";
        public object? Capture() => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public void Assembler_Gathers_Logs_And_Probe_Into_Evidence()
    {
        var probe = new HangProbe(31_200, DateTimeOffset.UtcNow, "ResolveInputs", "sqlite:ur_nodes", "outbox-writer:42");

        var ring = new RingBuffer<string>(200);
        ring.Add("line-a");
        ring.Add("line-b");

        var assembler = new HangEvidenceAssembler(ring, new[] { new StubSource() });
        var evidence = assembler.Assemble(probe, 31_200, HangVerdict.ConfirmedHang, "run-1", "rule_match");

        Assert.AreEqual(HangVerdict.ConfirmedHang, evidence.Verdict);
        Assert.AreEqual(31_200, evidence.StallMilliseconds);
        CollectionAssert.AreEqual(new[] { "line-a", "line-b" }, evidence.RecentLogs.ToArray());
        Assert.AreEqual("run-1", evidence.RunKey);
        Assert.AreEqual("rule_match", evidence.Step);
        Assert.AreEqual("sqlite:ur_nodes", evidence.Probe!.Resource);
        Assert.AreEqual(1, evidence.Context.Count);
        Assert.AreEqual("db", evidence.Context[0].Key);
    }

    [TestMethod]
    public void Assembler_CaptureFailure_Keeps_Full_StackTrace()
    {
        var assembler = new HangEvidenceAssembler(logs: null, sources: new[] { new ThrowingSource() });
        var evidence = assembler.Assemble(null, 1_000, HangVerdict.ConfirmedHang);

        Assert.AreEqual(1, evidence.Context.Count);
        var captured = evidence.Context[0].Value as string;
        Assert.IsNotNull(captured);
        StringAssert.Contains(captured, "<capture-failed:");
        // Stage 3：保留完整 ToString（含堆栈），而非仅 ex.Message，便于定位
        StringAssert.Contains(captured, "InvalidOperationException");
        StringAssert.Contains(captured, "boom");
        StringAssert.Contains(captured, "ThrowingSource");
    }
}
