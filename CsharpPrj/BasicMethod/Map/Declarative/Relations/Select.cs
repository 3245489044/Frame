
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Relations
{
    public class Select : RelationBase
    {
        private readonly IRelation _source;
        private readonly Func<IReadOnlyDictionary<string, object?>, bool> _predicate;

        public override IEnumerable<IReadOnlyDictionary<string, object?>> Tuples
            => _source.Tuples.Where(_predicate);

        public Select(
            string name,
            IRelation source,
            Func<IReadOnlyDictionary<string, object?>, bool> predicate)
            : base(name, source.Attributes)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public static Select Equal(
            string name,
            IRelation source,
            string attribute,
            object? value)
        {
            return new Select(name, source, tuple =>
            {
                if (!tuple.TryGetValue(attribute, out var actual))
                    return false;

                return Equals(actual, value);
            });
        }

        public static Select NotEqual(
            string name,
            IRelation source,
            string attribute,
            object? value)
        {
            return new Select(name, source, tuple =>
            {
                if (!tuple.TryGetValue(attribute, out var actual))
                    return true;

                return !Equals(actual, value);
            });
        }

        public static Select InRange<T>(
            string name,
            IRelation source,
            string attribute,
            T min,
            T max)
            where T : IComparable<T>
        {
            return new Select(name, source, tuple =>
            {
                if (!tuple.TryGetValue(attribute, out var raw) || raw is not T value)
                    return false;

                return value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
            });
        }
    }
}
