
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using SkeletonFlow.App;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Batch
{
    [TestClass]
    public class BatchSelectorTests : BatchTestBase
    {
        #region 当前批次测试

        [TestMethod]
        public void Test_Selector_GetCurrentBatch()
        {
            RunGroup("获取当前批次测试",
                ClearThen(
                    BatchAssertSkeleton.SelectorCurrentBatchEquals(null)),
                RegisterThen("batch_sel_curr", "CurrentBatch",
                    BatchAssertSkeleton.SelectorCurrentBatchEquals("batch_sel_curr"))
            );
        }

        #endregion

        #region 历史查看测试

        [TestMethod]
        public void Test_Selector_ViewHistory()
        {
            RunGroup("查看历史测试",
                ClearThen(
                    BatchAssertSkeleton.SelectorIsViewingHistory(false)),
                ClearThen(
                    BatchAssertSkeleton.Check("SwitchTo不同批次后进入历史查看", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_curr", "Current", "test");
                        BatchManager.Instance.RegisterBatch("batch_hist_view", "HistView", "test");
                        BatchSelector.SwitchTo("batch_curr");
                        return BatchSelector.IsViewingHistory();
                    })),
                ClearThen(
                    BatchAssertSkeleton.Check("ResetToCurrent后退出历史查看", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_hist_reset", "HistReset", "test");
                        BatchManager.Instance.RegisterBatch("batch_other", "Other", "test");
                        BatchSelector.SwitchTo("batch_hist_reset");
                        BatchSelector.ResetToCurrent();
                        return !BatchSelector.IsViewingHistory();
                    }))
            );
        }

        #endregion

        #region 列表显示测试

        [TestMethod]
        public void Test_Selector_GetBatchDisplayList()
        {
            RunGroup("获取批次列表测试",
                ClearThen(
                    BatchAssertSkeleton.SelectorListCountEquals(0)),
                RegisterThen("batch_sel_list1", "ListBatch1",
                    BatchAssertSkeleton.SelectorListCountEquals(1)),
                ClearThen(
                    BatchAssertSkeleton.Check("多批次列表", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_list_A", "ListA", "test");
                        BatchManager.Instance.RegisterBatch("batch_list_B", "ListB", "test");
                        var items = BatchSelector.GetBatchDisplayList();
                        return items.Count == 2
                            && items.Any(i => i.DisplayName.Contains("ListA"))
                            && items.Any(i => i.DisplayName.Contains("ListB"));
                    }))
            );
        }

        [TestMethod]
        public void Test_Selector_DisplayList_Order()
        {
            RunGroup("列表排序测试",
                ClearThen(
                    BatchAssertSkeleton.Check("按时间倒序排列", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_order_1", "First", "test");
                        Thread.Sleep(10);
                        BatchManager.Instance.RegisterBatch("batch_order_2", "Second", "test");
                        Thread.Sleep(10);
                        BatchManager.Instance.RegisterBatch("batch_order_3", "Third", "test");

                        var items = BatchSelector.GetBatchDisplayList();
                        return items.Count == 3
                            && items[0].DisplayName.Contains("Third")
                            && items[1].DisplayName.Contains("Second")
                            && items[2].DisplayName.Contains("First");
                    }))
            );
        }

        #endregion

        #region 切换批次测试

        [TestMethod]
        public void Test_Selector_SwitchTo()
        {
            RunGroup("切换批次测试",
                RegisterThen("batch_sel_switch", "SwitchBatch",
                    BatchAssertSkeleton.SelectorCurrentBatchEquals("batch_sel_switch")),
                ClearThen(
                    BatchAssertSkeleton.Check("SwitchTo null回到当前", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_switch_null", "SwitchNull", "test");
                        BatchSelector.SwitchTo("batch_switch_null");
                        BatchSelector.SwitchTo(null);
                        return !BatchSelector.IsViewingHistory()
                            && BatchSelector.GetCurrentBatch() == "batch_switch_null";
                    })),
                ClearThen(
                    BatchAssertSkeleton.Check("SwitchTo不存在批次", () =>
                    {
                        BatchSelector.SwitchTo("nonexistent");
                        return BatchSelector.GetCurrentBatch() == "nonexistent";
                    }))
            );
        }

        #endregion

        #region 重置测试

        [TestMethod]
        public void Test_Selector_ResetToCurrent()
        {
            RunGroup("重置到当前测试",
                RegisterThen("batch_sel_reset", "ResetBatch",
                    BatchAssertSkeleton.SelectorCurrentBatchEquals("batch_sel_reset")),
                ClearThen(
                    BatchAssertSkeleton.Check("重置后退出历史查看", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_sel_reset2", "ResetBatch2", "test");
                        BatchManager.Instance.RegisterBatch("batch_sel_other", "Other", "test");
                        BatchSelector.SwitchTo("batch_sel_reset2");
                        BatchSelector.ResetToCurrent();
                        return !BatchSelector.IsViewingHistory();
                    }))
            );
        }

        #endregion

        #region 多批次切换测试

        [TestMethod]
        public void Test_Selector_MultipleBatches_Switch()
        {
            RunGroup("多批次切换测试",
                ClearThen(
                    BatchAssertSkeleton.Check("多批次间切换", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_sel_multi_A", "MultiA", "test");
                        BatchManager.Instance.RegisterBatch("batch_sel_multi_B", "MultiB", "test");

                        BatchSelector.SwitchTo("batch_sel_multi_A");
                        var currentA = BatchSelector.GetCurrentBatch();

                        BatchSelector.SwitchTo("batch_sel_multi_B");
                        var currentB = BatchSelector.GetCurrentBatch();

                        return currentA == "batch_sel_multi_A" && currentB == "batch_sel_multi_B";
                    }))
            );
        }

        #endregion

        #region 当前批次显示名称测试

        [TestMethod]
        public void Test_Selector_GetCurrentBatchDisplayName()
        {
            RunGroup("当前批次显示名称测试",
                ClearThen(
                    BatchAssertSkeleton.Check("无批次时返回默认文本", () =>
                    {
                        var name = BatchSelector.GetCurrentBatchDisplayName();
                        return name == "（无批次）";
                    })),
                RegisterThen("batch_sel_disp", "DisplayBatch",
                    BatchAssertSkeleton.Check("有批次时返回名称", () =>
                    {
                        var name = BatchSelector.GetCurrentBatchDisplayName();
                        return name.Contains("DisplayBatch");
                    }))
            );
        }

        #endregion

        #region 按索引获取批次 ID 测试

        [TestMethod]
        public void Test_Selector_GetBatchIdByIndex()
        {
            RunGroup("按索引获取批次ID测试",
                ClearThen(
                    BatchAssertSkeleton.Check("按索引获取批次ID", () =>
                    {
                        BatchManager.Instance.RegisterBatch("batch_idx_1", "Index1", "test");
                        BatchManager.Instance.RegisterBatch("batch_idx_2", "Index2", "test");

                        var idByIndex1 = BatchSelector.GetBatchIdByIndex(1);
                        var idByIndex2 = BatchSelector.GetBatchIdByIndex(2);

                        return idByIndex1 == "batch_idx_2"
                            && idByIndex2 == "batch_idx_1";
                    })),
                ClearThen(
                    BatchAssertSkeleton.Check("无效索引返回null", () =>
                    {
                        var id = BatchSelector.GetBatchIdByIndex(999);
                        return id == null;
                    }))
            );
        }

        #endregion
    }
}
