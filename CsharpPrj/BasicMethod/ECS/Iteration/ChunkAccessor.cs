namespace ECS.Core
{
    public ref struct ChunkAccessor<T> where T : struct, IComponent
    {
        private readonly Span<T> _components;
        private readonly Span<Entity> _entities;
        private readonly int _length;

        internal ChunkAccessor(Span<T> components, Span<Entity> entities)
        {
            _components = components;
            _entities = entities;
            _length = Math.Min(components.Length, entities.Length);
        }

        public int Length => _length;

        public ref T this[int index] => ref _components[index];

        public Entity GetEntity(int index) => _entities[index];

        public Span<T> ComponentSpan => _components;

        public Span<Entity> EntitySpan => _entities;
    }

    public ref struct ChunkAccessor<T1, T2>
        where T1 : struct, IComponent
        where T2 : struct, IComponent
    {
        private readonly Span<T1> _c1;
        private readonly Span<T2> _c2;
        private readonly Span<Entity> _entities;
        private readonly int _length;

        internal ChunkAccessor(Span<T1> c1, Span<T2> c2, Span<Entity> entities)
        {
            _c1 = c1;
            _c2 = c2;
            _entities = entities;
            _length = Math.Min(c1.Length, Math.Min(c2.Length, entities.Length));
        }

        public int Length => _length;
        public Entity GetEntity(int index) => _entities[index];
        public ref T1 C1 => ref _c1[0];
        public ref T2 C2 => ref _c2[0];
        public Span<T1> Span1 => _c1;
        public Span<T2> Span2 => _c2;
    }

    public ref struct ChunkAccessor<T1, T2, T3>
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent
    {
        private readonly Span<T1> _c1;
        private readonly Span<T2> _c2;
        private readonly Span<T3> _c3;
        private readonly Span<Entity> _entities;
        private readonly int _length;

        internal ChunkAccessor(Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<Entity> entities)
        {
            _c1 = c1;
            _c2 = c2;
            _c3 = c3;
            _entities = entities;
            _length = Math.Min(c1.Length, Math.Min(c2.Length, Math.Min(c3.Length, entities.Length)));
        }

        public int Length => _length;
        public Entity GetEntity(int index) => _entities[index];
        public ref T1 C1 => ref _c1[0];
        public ref T2 C2 => ref _c2[0];
        public ref T3 C3 => ref _c3[0];
        public Span<T1> Span1 => _c1;
        public Span<T2> Span2 => _c2;
        public Span<T3> Span3 => _c3;
    }
}
