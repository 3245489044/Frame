using TConfig.ContextFrame.Abstractions;

namespace ECS.Core
{
    public sealed class ComponentTypeId
    {
        private readonly IStorageRegistry _registry;

        private const string TypeToIdPrefix = "comp_type_to_id:";
        private const string IdToTypePrefix = "comp_id_to_type:";
        private const string CounterName = "ComponentTypeId";

        public ComponentTypeId(IStorageRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public int GetId(Type type)
        {
            var key = $"{TypeToIdPrefix}{type.FullName}";
            if (_registry.TryResolve<int>(key, out var existingId))
                return existingId;

            int id = _registry.NextId(CounterName);
            _registry.Register(key, id);
            _registry.Register($"{IdToTypePrefix}{id}", type.FullName!);
            return id;
        }

        public Type? GetType(int id)
        {
            var key = $"{IdToTypePrefix}{id}";
            if (_registry.TryResolve<string>(key, out var typeName) && typeName != null)
            {
                var type = System.Type.GetType(typeName);
                if (type != null) return type;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName);
                    if (type != null) return type;
                }
            }
            return null;
        }

        public int Count => _registry.CurrentId(CounterName);
    }
}
