
using System.Collections.Concurrent;
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ObjectFactory : IObjectFactory
    {
        private readonly ConcurrentDictionary<string, Func<object>> _factories = new();

        public virtual object Create(string typeName)
        {
            ArgumentNullException.ThrowIfNull(typeName);

            if (_factories.TryGetValue(typeName, out var factory))
            {
                return factory();
            }

            var type = Type.GetType(typeName);
            if (type != null)
            {
                return Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"无法创建类型 '{typeName}' 的实例。");
            }

            throw new KeyNotFoundException($"未注册类型 '{typeName}'，且无法通过反射找到该类型。");
        }

        public T Create<T>(string name) where T : new()
        {
            var obj = Create(name);
            return (T)obj;
        }

        public object Create(Type type, string name)
        {
            ArgumentNullException.ThrowIfNull(type);

            var obj = Create(name);
            if (!type.IsInstanceOfType(obj))
            {
                throw new InvalidCastException($"创建的对象类型 '{obj.GetType().Name}' 无法转换为 '{type.Name}'。");
            }
            return obj;
        }

        public void RegisterType(string typeName, Func<object> factory)
        {
            ArgumentNullException.ThrowIfNull(typeName);
            ArgumentNullException.ThrowIfNull(factory);

            _factories[typeName] = factory;
        }

        public bool UnregisterType(string typeName)
        {
            ArgumentNullException.ThrowIfNull(typeName);
            return _factories.TryRemove(typeName, out _);
        }

        public string[] GetRegisteredTypes()
        {
            return _factories.Keys.ToArray();
        }
    }
}
