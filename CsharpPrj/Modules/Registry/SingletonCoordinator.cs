
using System.Runtime.CompilerServices;

namespace Share.Registry
{
    public static class SingletonCoordinator<T> where T : class
    {
        private static T? _instance;
        private static Func<T>? _factory;
        private static readonly object _lock = new();
        private static volatile bool _isSet;

        public static T Instance
        {
            get
            {
                if (_isSet) return _instance!;
                lock (_lock)
                {
                    if (!_isSet)
                    {
                        if (_factory != null)
                        {
                            _instance = _factory();
                        }
                        else
                        {
                            if (typeof(T).IsInterface)
                            {
                                throw new InvalidOperationException(
                                    $"SingletonCoordinator<{typeof(T).Name}>: 类型参数是接口 {typeof(T).FullName}，" +
                                    $"无法自动创建实例。请在使用 Instance 之前调用 Set() 注入实例，或通过 SetDefaultFactory() 设置工厂函数。");
                            }
                            _instance = Activator.CreateInstance<T>();
                        }
                        _isSet = true;
                    }
                    return _instance!;
                }
            }
        }

        public static bool IsSet => _isSet;

        public static void SetDefaultFactory(Func<T> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        public static void Set(T instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
            lock (_lock)
            {
                _instance = instance;
                _isSet = true;
            }
        }

        public static bool TryGet(out T? instance)
        {
            if (_isSet)
            {
                instance = _instance;
                return true;
            }
            instance = null;
            return false;
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
                _isSet = false;
            }
        }
    }
}
