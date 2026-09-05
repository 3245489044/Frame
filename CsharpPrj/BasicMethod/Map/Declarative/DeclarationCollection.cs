
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative
{
    public class DeclarationCollection<TDeclaration> : IDeclarationCollection<TDeclaration>
        where TDeclaration : IDeclaration
    {
        private readonly List<TDeclaration> _items = new();
        private readonly object _lock = new();

        public IReadOnlyList<TDeclaration> Items
        {
            get
            {
                lock (_lock)
                {
                    return _items.ToList().AsReadOnly();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_lock) { return _items.Count; }
            }
        }

        public IDeclarationCollection<TDeclaration> Register(TDeclaration item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                _items.Add(item);
            }
            return this;
        }

        public IDeclarationCollection<TDeclaration> RegisterRange(IEnumerable<TDeclaration> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            lock (_lock)
            {
                _items.AddRange(items);
            }
            return this;
        }

        public TDeclaration? Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return default;

            lock (_lock)
            {
                return _items.FirstOrDefault(d =>
                    string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public IEnumerable<TDeclaration> FindByType(string declarationType)
        {
            if (string.IsNullOrWhiteSpace(declarationType))
                return Enumerable.Empty<TDeclaration>();

            lock (_lock)
            {
                return _items
                    .Where(d => string.Equals(
                        d.DeclarationType, declarationType, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        public IDeclarationCollection<TDeclaration> Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return this;

            lock (_lock)
            {
                _items.RemoveAll(d =>
                    string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            return this;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }
    }
}
