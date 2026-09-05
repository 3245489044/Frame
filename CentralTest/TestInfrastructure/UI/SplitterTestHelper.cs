
using System;
using System.Windows.Forms;

namespace TestInfrastructure
{
    public static class SplitterTestHelper
    {
        private static T RunOnUi<T>(Func<T> action) => UiTestHelper.RunOnUi(action);

        private static void RunOnUi(Action action) => UiTestHelper.RunOnUi(action);

        public static int GetSplitterDistance(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.SplitterDistance);
        }

        public static void SetSplitterDistance(SplitContainer splitContainer, int distance)
        {
            RunOnUi(() => splitContainer.SplitterDistance = distance);
        }

        public static Orientation GetOrientation(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Orientation);
        }

        public static SplitterPanel GetPanel1(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel1);
        }

        public static SplitterPanel GetPanel2(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel2);
        }

        public static int GetPanel1MinSize(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel1MinSize);
        }

        public static int GetPanel2MinSize(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel2MinSize);
        }

        public static bool IsSplitterFixed(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.IsSplitterFixed);
        }

        public static void SetSplitterFixed(SplitContainer splitContainer, bool isFixed)
        {
            RunOnUi(() => splitContainer.IsSplitterFixed = isFixed);
        }

        public static int GetSplitterWidth(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.SplitterWidth);
        }

        public static bool IsSplitterVisible(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.SplitterRectangle.Width > 0 && splitContainer.SplitterRectangle.Height > 0);
        }

        public static bool IsPanel1Collapsed(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel1Collapsed);
        }

        public static void SetPanel1Collapsed(SplitContainer splitContainer, bool collapsed)
        {
            RunOnUi(() => splitContainer.Panel1Collapsed = collapsed);
        }

        public static bool IsPanel2Collapsed(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel2Collapsed);
        }

        public static void SetPanel2Collapsed(SplitContainer splitContainer, bool collapsed)
        {
            RunOnUi(() => splitContainer.Panel2Collapsed = collapsed);
        }

        public static int GetPanel1Length(SplitContainer splitContainer)
        {
            return RunOnUi(() =>
                splitContainer.Orientation == Orientation.Vertical
                    ? splitContainer.Panel1.Width
                    : splitContainer.Panel1.Height);
        }

        public static int GetPanel2Length(SplitContainer splitContainer)
        {
            return RunOnUi(() =>
                splitContainer.Orientation == Orientation.Vertical
                    ? splitContainer.Panel2.Width
                    : splitContainer.Panel2.Height);
        }

        public static bool IsSplitterDistanceValid(SplitContainer splitContainer)
        {
            return RunOnUi(() =>
            {
                var distance = splitContainer.SplitterDistance;
                var minDistance = splitContainer.Panel1MinSize;
                var maxDistance = (splitContainer.Orientation == Orientation.Vertical
                    ? splitContainer.Width
                    : splitContainer.Height) - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
                return distance >= minDistance && distance <= maxDistance;
            });
        }

        public static int GetPanel1ControlCount(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel1.Controls.Count);
        }

        public static int GetPanel2ControlCount(SplitContainer splitContainer)
        {
            return RunOnUi(() => splitContainer.Panel2.Controls.Count);
        }

        public static T? GetPanel1Control<T>(SplitContainer splitContainer) where T : Control
        {
            return RunOnUi(() =>
            {
                foreach (Control c in splitContainer.Panel1.Controls)
                {
                    if (c is T t)
                        return t;
                }
                return null;
            });
        }

        public static T? GetPanel2Control<T>(SplitContainer splitContainer) where T : Control
        {
            return RunOnUi(() =>
            {
                foreach (Control c in splitContainer.Panel2.Controls)
                {
                    if (c is T t)
                        return t;
                }
                return null;
            });
        }
    }
}
