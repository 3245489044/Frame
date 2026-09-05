
namespace TConfig.ECS;

public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Id, Version;

    public Entity(int id, int version) => (Id, Version) = (id, version);

    public bool Equals(Entity other) => Id == other.Id && Version == other.Version;
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Version);
    public override string ToString() => $"Entity({Id}:{Version})";

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

    public static readonly Entity Invalid = new(-1, -1);
    public bool IsValid => Id >= 0 && Version >= 0;

}
