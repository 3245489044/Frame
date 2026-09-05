using System.Collections;

namespace ECS.Core
{
    public readonly struct ArchetypeSignature : IEquatable<ArchetypeSignature>

    {
        public readonly BitArray Components;
        public readonly int HashCode;

        public ArchetypeSignature()
        {
            Components = new BitArray(0);
            HashCode = ComputeHash(Components);
        }

        public ArchetypeSignature(Type[] componentTypes, ComponentTypeId componentTypeId)
        {
            if (componentTypes.Length == 0)
            {
                Components = new BitArray(0);
            }
            else
            {
                int maxId = 0;
                foreach (var t in componentTypes)
                    maxId = Math.Max(maxId, componentTypeId.GetId(t));
                Components = new BitArray(maxId + 1);
                foreach (var t in componentTypes)
                    Components.Set(componentTypeId.GetId(t), true);
            }
            HashCode = ComputeHash(Components);
        }

        public ArchetypeSignature(BitArray components)
        {
            Components = components ?? throw new ArgumentNullException(nameof(components));
            HashCode = ComputeHash(Components);
        }

        public bool Equals(ArchetypeSignature other)
        {
            if (HashCode != other.HashCode) return false;
            if (Components.Length != other.Components.Length)
                return false;
            for (int i = 0; i < Components.Length; i++)
                if (Components[i] != other.Components[i])
                    return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ArchetypeSignature other && Equals(other);

        public override int GetHashCode() => HashCode;

        public override string ToString()
        {
            var types = new List<string>();
            for (int i = 0; i < Components.Length; i++)
                if (Components[i])
                    types.Add($"T{i}");
            return types.Count > 0 ? string.Join(", ", types) : "[empty]";
        }

        public ArchetypeSignature WithComponent(int typeId, bool value)
        {
            int newLen = Math.Max(Components.Length, typeId + 1);
            var newBits = new BitArray(newLen);
            for (int i = 0; i < Components.Length; i++)
                newBits[i] = Components[i];
            newBits.Set(typeId, value);
            return new ArchetypeSignature(newBits);
        }

        public bool IsSubsetOf(ArchetypeSignature other)
        {
            int minLen = Math.Min(Components.Length, other.Components.Length);
            for (int i = 0; i < minLen; i++)
                if (Components[i] && !other.Components[i])
                    return false;

            if (Components.Length > other.Components.Length)
            {
                for (int i = other.Components.Length; i < Components.Length; i++)
                    if (Components[i])
                        return false;
            }

            return true;
        }

        private static int ComputeHash(BitArray bits)
        {
            int hash = 17;
            for (int i = 0; i < bits.Length; i++)
                hash = hash * 31 + (bits[i] ? 1 : 0);
            return hash;
        }
    }

}
