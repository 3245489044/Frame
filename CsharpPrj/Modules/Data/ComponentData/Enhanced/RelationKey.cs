namespace Modules.ComponentData.Enhanced
{
    public readonly struct RelationKey : IEquatable<RelationKey>
    {
        public Type Type { get; }
        public long EnumValue { get; }

        public RelationKey(object enumValue)
        {
            if (enumValue == null) throw new ArgumentNullException(nameof(enumValue));
            Type = enumValue.GetType();
            if (!Type.IsEnum) throw new ArgumentException("Value must be an enum.", nameof(enumValue));
            EnumValue = Convert.ToInt64(enumValue);
        }

        public RelationKey(Type type)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            EnumValue = 0;
        }

        public bool Equals(RelationKey other) => Type == other.Type && EnumValue == other.EnumValue;
        public override bool Equals(object? obj) => obj is RelationKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Type, EnumValue);
        public override string ToString() => EnumValue != 0 ? $"{Type.Name}.{EnumValue}" : Type.Name;
    }
}
