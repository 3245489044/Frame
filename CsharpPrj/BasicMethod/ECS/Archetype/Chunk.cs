namespace ECS.Core
{
    public sealed class Chunk
    {
        public const int Capacity = 64;

        private int _count;
        private Entity[] _entities = new Entity[Capacity];
        private readonly Dictionary<int, Array> _componentColumns = new();

        public int Count => _count;
        public bool IsFull => _count >= Capacity;
        public bool IsEmpty => _count == 0;

        public Span<Entity> EntitySpan => new(_entities, 0, _count);

        public int AddEntity(Entity entity, IComponent[] components, ComponentTypeId componentTypeId)
        {
            if (IsFull)
                throw new InvalidOperationException("Chunk is full");

            int index = _count;
            _entities[index] = entity;

            foreach (var component in components)
            {
                int typeId = componentTypeId.GetId(component.GetType());
                if (!_componentColumns.TryGetValue(typeId, out var column))
                {
                    column = Array.CreateInstance(component.GetType(), Capacity);
                    _componentColumns[typeId] = column;
                }
                column.SetValue(component, index);
            }

            _count++;
            return index;
        }

        public void RemoveEntity(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int lastIndex = _count - 1;

            _entities[index] = _entities[lastIndex];
            foreach (var kvp in _componentColumns)
            {
                var src = kvp.Value;
                src.SetValue(src.GetValue(lastIndex), index);
            }

            _count--;
        }

        public T GetComponent<T>(int index, ComponentTypeId componentTypeId) where T : struct, IComponent
        {
            int typeId = componentTypeId.GetId(typeof(T));
            if (!_componentColumns.TryGetValue(typeId, out var column))
                throw new InvalidOperationException($"Component {typeof(T).Name} not in this chunk");
            return (T)column.GetValue(index)!;
        }

        public void SetComponent<T>(int index, T component, ComponentTypeId componentTypeId) where T : struct, IComponent
        {
            int typeId = componentTypeId.GetId(typeof(T));
            if (!_componentColumns.TryGetValue(typeId, out var column))
                throw new InvalidOperationException($"Component {typeof(T).Name} not in this chunk");
            column.SetValue(component, index);
        }

        public bool HasComponent<T>(ComponentTypeId componentTypeId) where T : struct, IComponent
            => HasComponent(componentTypeId.GetId(typeof(T)));

        public bool HasComponent(int typeId)
        {
            return _componentColumns.ContainsKey(typeId);
        }

        public Span<T> GetComponentSpan<T>(ComponentTypeId componentTypeId) where T : struct, IComponent
        {
            int typeId = componentTypeId.GetId(typeof(T));
            if (!_componentColumns.TryGetValue(typeId, out var column))
                return Span<T>.Empty;

            if (column.GetType().GetElementType() != typeof(T))
                throw new InvalidCastException(
                    $"ComponentSpan type mismatch: expected {typeof(T).Name}, actual {column.GetType().GetElementType()?.Name}");

            return new Span<T>((T[])column, 0, _count);
        }

        public Entity GetEntity(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _entities[index];
        }

        public int FindEntityIndex(Entity entity)
        {
            for (int i = 0; i < _count; i++)
                if (_entities[i] == entity)
                    return i;
            return -1;
        }

        public void Clear()
        {
            Array.Clear(_entities, 0, _count);
            foreach (var column in _componentColumns.Values)
                Array.Clear(column, 0, _count);
            _count = 0;
        }

        internal IReadOnlyDictionary<int, Array> GetComponentColumns() => _componentColumns;

        internal IComponent[] GetComponentsAt(int index)
        {
            var result = new IComponent[_componentColumns.Count];
            int i = 0;
            foreach (var kvp in _componentColumns)
            {
                var column = kvp.Value;
                var value = column.GetValue(index);
                result[i++] = value is IComponent comp
                    ? comp
                    : throw new InvalidCastException(
                        $"Column value at index {index} for type ID {kvp.Key} is not an IComponent");
            }
            return result;
        }

        internal IEnumerable<int> GetComponentTypeIds() => _componentColumns.Keys;
    }
}
