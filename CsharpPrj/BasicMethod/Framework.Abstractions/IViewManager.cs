
namespace Modules.Framework.Abstractions
{
    public enum ViewState
    {
        Unloaded,
        Loading,
        Loaded,
        Unloading,
        UnloadedComplete
    }

    public class ViewInfo<TView>
    {
        public string Name { get; }

        public TView? View { get; set; }

        public ViewState State { get; set; } = ViewState.Unloaded;

        public bool IsActive { get; set; }

        public ViewInfo(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    public interface IViewManager<TView>
    {
        TView? ActiveView { get; }

        IReadOnlyList<ViewInfo<TView>> Views { get; }

        void RegisterView(string name, TView view);

        void RegisterViewFactory(string name, Func<TView> factory);

        bool ActivateView(string name);

        bool UnloadView(string name);

        TView? GetView(string name);

        event EventHandler<ViewInfo<TView>>? ViewActivated;

        event EventHandler<ViewInfo<TView>>? ViewUnloaded;
    }
}
