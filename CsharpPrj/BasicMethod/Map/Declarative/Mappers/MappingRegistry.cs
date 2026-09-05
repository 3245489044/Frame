
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Mappers
{
    public class MappingRegistry
    {
        private readonly Dictionary<string, object> _mappers = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) { return _mappers.Count; } }
        }

        public MappingRegistry Register<TDeclaration, TRuntime>(
            IDeclarativeMapper<TDeclaration, TRuntime> mapper)
            where TDeclaration : IDeclaration
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            lock (_lock)
            {
                _mappers[mapper.Name] = mapper;
            }
            return this;
        }

        public IDeclarativeMapper<TDeclaration, TRuntime>? Find<TDeclaration, TRuntime>(string name)
            where TDeclaration : IDeclaration
        {
            lock (_lock)
            {
                if (_mappers.TryGetValue(name, out var mapper) &&
                    mapper is IDeclarativeMapper<TDeclaration, TRuntime> typed)
                {
                    return typed;
                }
            }
            return null;
        }

        public IEnumerable<IDeclarativeMapper<TDeclaration, TRuntime>> FindByDeclarationType<TDeclaration, TRuntime>()
            where TDeclaration : IDeclaration
        {
            List<IDeclarativeMapper<TDeclaration, TRuntime>> result;
            lock (_lock)
            {
                result = _mappers.Values
                    .OfType<IDeclarativeMapper<TDeclaration, TRuntime>>()
                    .ToList();
            }
            return result;
        }

        public IReadOnlyList<string> GetMapperNames()
        {
            lock (_lock)
            {
                return _mappers.Keys.ToList().AsReadOnly();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _mappers.Clear();
            }
        }
    }
}
