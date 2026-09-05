
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ViewManager<TView> : IViewManager<TView>
    {
        private readonly Dictionary<string, ViewInfo<TView>> _views = new();
        private readonly Dictionary<string, Func<TView>> _factories = new();
        private readonly object _lock = new();

        public TView? ActiveView { get; private set; }

        public IReadOnlyList<ViewInfo<TView>> Views
        {
            get
            {
                lock (_lock)
                {
                    return _views.Values.ToList().AsReadOnly();
                }
            }
        }

        public event EventHandler<ViewInfo<TView>>? ViewActivated;

        public event EventHandler<ViewInfo<TView>>? ViewUnloaded;

        public void RegisterView(string name, TView view)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(view);

            lock (_lock)
            {
                if (_views.ContainsKey(name))
                    throw new InvalidOperationException($"视图 '{name}' 已注册。");

                _views[name] = new ViewInfo<TView>(name) { View = view, State = ViewState.Loaded };
            }
        }

        public void RegisterViewFactory(string name, Func<TView> factory)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(factory);

            lock (_lock)
            {
                if (_views.ContainsKey(name))
                    throw new InvalidOperationException($"视图 '{name}' 已注册。");

                _factories[name] = factory;
                _views[name] = new ViewInfo<TView>(name) { State = ViewState.Unloaded };
            }
        }

        public bool ActivateView(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            ViewInfo<TView>? info;

            lock (_lock)
            {
                if (!_views.TryGetValue(name, out info))
                    return false;

                if (info.View == null && _factories.TryGetValue(name, out var factory))
                {
                    info.State = ViewState.Loading;
                    info.View = factory();
                    info.State = ViewState.Loaded;
                }

                if (info.View == null)
                    return false;

                if (ActiveView != null)
                {
                    var oldActive = _views.Values.FirstOrDefault(v => v.IsActive);
                    if (oldActive != null)
                    {
                        oldActive.IsActive = false;
                    }
                }

                info.IsActive = true;
                ActiveView = info.View;
            }

            ViewActivated?.Invoke(this, info);
            return true;
        }

        public bool UnloadView(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            ViewInfo<TView>? info;

            lock (_lock)
            {
                if (!_views.TryGetValue(name, out info))
                    return false;

                info.State = ViewState.Unloading;
                info.View = default;
                info.IsActive = false;
                info.State = ViewState.UnloadedComplete;

                if (ActiveView != null && ReferenceEquals(ActiveView, info.View))
                {
                    ActiveView = default;
                }
            }

            ViewUnloaded?.Invoke(this, info);
            return true;
        }

        public TView? GetView(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            lock (_lock)
            {
                return _views.TryGetValue(name, out var info) ? info.View : default;
            }
        }
    }
}
