
namespace Modules.Framework.Abstractions
{
    public class NavigationEventArgs<TView> : EventArgs
    {
        public TView? Target { get; }

        public bool CanGoBack { get; }

        public bool CanGoForward { get; }

        public NavigationEventArgs(TView? target, bool canGoBack, bool canGoForward)
        {
            Target = target;
            CanGoBack = canGoBack;
            CanGoForward = canGoForward;
        }
    }

    public interface INavigationService<TView>
    {
        TView? CurrentView { get; }

        bool CanGoBack { get; }

        bool CanGoForward { get; }

        void NavigateTo(TView view);

        void NavigateBack();

        void NavigateForward();

        void ClearHistory();

        event EventHandler<NavigationEventArgs<TView>>? NavigationChanged;
    }
}
