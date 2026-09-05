
namespace Modules.Framework.Abstractions
{
    #region 布局基础类型

    public struct LayoutRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public LayoutRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }

    public class LayoutItemInfo<TItem>
    {
        public TItem Item { get; }

        public LayoutRect Bounds { get; set; }

        public int ZOrder { get; set; }

        public LayoutItemInfo(TItem item, LayoutRect bounds, int zOrder = 0)
        {
            Item = item;
            Bounds = bounds;
            ZOrder = zOrder;
        }
    }

    public class LayoutResult<TItem>
    {
        public List<LayoutItemInfo<TItem>> Items { get; } = new();

        public bool Success { get; set; } = true;

        public string? ErrorMessage { get; set; }

        public TimeSpan Elapsed { get; set; }
    }

    #endregion

    #region 布局上下文

    public class LayoutContext<TContainer, TItem>
    {
        public TContainer Container { get; }

        public int ContainerWidth { get; }

        public int ContainerHeight { get; }

        public IReadOnlyList<TItem> Items { get; }

        public LayoutContext(TContainer container, int width, int height, IReadOnlyList<TItem> items)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            ContainerWidth = width;
            ContainerHeight = height;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }
    }

    #endregion

    #region 布局策略

    public enum LayoutStrategyType
    {
        Proportional,
        Stack,
        Flow,
        Table,
        Custom
    }

    public interface ILayoutStrategy<TContainer, TItem>
    {
        LayoutStrategyType StrategyType { get; }

        string Name { get; }

        LayoutResult<TItem> Calculate(LayoutContext<TContainer, TItem> context);
    }

    #endregion

    #region 布局执行器

    public interface ILayoutExecutor<TContainer, TItem>
    {
        void RegisterStrategy(ILayoutStrategy<TContainer, TItem> strategy);

        bool UnregisterStrategy(LayoutStrategyType type);

        LayoutResult<TItem> Execute(LayoutContext<TContainer, TItem> context);
    }

    #endregion
}
