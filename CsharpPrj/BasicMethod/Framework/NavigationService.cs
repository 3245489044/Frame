
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class NavigationService<TView> : INavigationService<TView>
    {
        private readonly Stack<TView> _backStack = new();
        private readonly Stack<TView> _forwardStack = new();

        public TView? CurrentView { get; private set; }

        public bool CanGoBack => _backStack.Count > 0;

        public bool CanGoForward => _forwardStack.Count > 0;

        public event EventHandler<NavigationEventArgs<TView>>? NavigationChanged;

        public void NavigateTo(TView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            if (CurrentView != null)
            {
                _backStack.Push(CurrentView);
            }

            _forwardStack.Clear();
            CurrentView = view;

            OnNavigationChanged();
        }

        public void NavigateBack()
        {
            if (!CanGoBack)
                throw new InvalidOperationException("无法回退：历史栈为空。");

            if (CurrentView != null)
            {
                _forwardStack.Push(CurrentView);
            }

            CurrentView = _backStack.Pop();
            OnNavigationChanged();
        }

        public void NavigateForward()
        {
            if (!CanGoForward)
                throw new InvalidOperationException("无法前进：历史栈为空。");

            if (CurrentView != null)
            {
                _backStack.Push(CurrentView);
            }

            CurrentView = _forwardStack.Pop();
            OnNavigationChanged();
        }

        public void ClearHistory()
        {
            _backStack.Clear();
            _forwardStack.Clear();
        }

        private void OnNavigationChanged()
        {
            NavigationChanged?.Invoke(this, new NavigationEventArgs<TView>(
                CurrentView, CanGoBack, CanGoForward));
        }
    }
}
