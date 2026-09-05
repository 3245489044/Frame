using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.TaskPool;

namespace Share.StateManagement.Tests;

/// <summary>
/// 任务状态镜像单测（canonical 于 Share.TaskStatusPool.TaskPool.TaskPoolStateMirror，SingleImplementation / TH-12）。
/// 覆盖：直读直写、订阅事件累积、换源重订退订旧源、Detach 停更、键序快照。
/// </summary>
[TestClass]
public class TaskPoolStateMirrorTests
{
    private sealed class FakeSource : ITaskPoolStateSource
    {
        public event EventHandler<TaskPoolStateChangedEventArgs>? TaskStateChanged;

        public void Raise(string key, TaskExecutionStatus to)
            => TaskStateChanged?.Invoke(this, new TaskPoolStateChangedEventArgs(key, TaskExecutionStatus.NotStarted, to));
    }

    [TestMethod]
    public void Set_Remove_TryGet_DirectWrites()
    {
        var mirror = new TaskPoolStateMirror();
        mirror.Set("a", "Running");
        Assert.AreEqual("Running", mirror.TryGet("a"));
        Assert.AreEqual(1, mirror.Count);
        Assert.IsTrue(mirror.Remove("a"));
        Assert.IsNull(mirror.TryGet("a"));
        Assert.AreEqual(0, mirror.Count);
        Assert.IsFalse(mirror.Remove("a"));
    }

    [TestMethod]
    public void Subscribe_Event_AccumulatesMirror()
    {
        var src = new FakeSource();
        var mirror = new TaskPoolStateMirror();
        mirror.EnsureSubscribed(src);
        src.Raise("t1", TaskExecutionStatus.Running);
        src.Raise("t1", TaskExecutionStatus.Paused);
        src.Raise("t2", TaskExecutionStatus.Completed);
        Assert.AreEqual("Paused", mirror.TryGet("t1"));
        Assert.AreEqual("Completed", mirror.TryGet("t2"));
        Assert.AreEqual(2, mirror.Count);
    }

    [TestMethod]
    public void EnsureSubscribed_SwapSource_UnsubscribesOld()
    {
        var srcA = new FakeSource();
        var srcB = new FakeSource();
        var mirror = new TaskPoolStateMirror();
        mirror.EnsureSubscribed(srcA);
        srcA.Raise("k", TaskExecutionStatus.Running);
        mirror.EnsureSubscribed(srcB); // 换源 → 退订 A
        srcA.Raise("k", TaskExecutionStatus.Paused); // A 不再被观测
        srcB.Raise("k", TaskExecutionStatus.Completed);
        Assert.AreEqual("Completed", mirror.TryGet("k"));
    }

    [TestMethod]
    public void Detach_StopsUpdates()
    {
        var src = new FakeSource();
        var mirror = new TaskPoolStateMirror();
        mirror.EnsureSubscribed(src);
        src.Raise("k", TaskExecutionStatus.Running);
        mirror.Detach(src);
        src.Raise("k", TaskExecutionStatus.Cancelled);
        Assert.AreEqual("Running", mirror.TryGet("k"));
    }

    [TestMethod]
    public void Entries_TaskKeys_SortedByKey()
    {
        var mirror = new TaskPoolStateMirror();
        mirror.Set("b", "Running");
        mirror.Set("a", "Paused");
        mirror.Set("c", "Completed");
        var entries = mirror.Entries();
        Assert.AreEqual("a", entries[0].Key);
        Assert.AreEqual("b", entries[1].Key);
        Assert.AreEqual("c", entries[2].Key);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, mirror.TaskKeys.ToArray());
    }
}
