
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Modules.ComponentData.Reflection
{
    public class InvocationDescriptor : IEquatable<InvocationDescriptor>
    {
        public Type? TargetType { get; set; }
        public string? MemberName { get; set; }
        public MemberKind MemberKind { get; set; }
        public Type[]? ParameterTypes { get; set; }
        public Type[]? GenericArguments { get; set; }
        public bool IsStatic { get; set; }
        public bool IsSetter { get; set; }

        public bool Equals(InvocationDescriptor? other)
        {
            if (other == null) return false;
            if (!Equals(TargetType, other.TargetType)) return false;
            if (MemberKind != other.MemberKind) return false;
            if (MemberName != other.MemberName) return false;
            if (IsStatic != other.IsStatic) return false;
            if (IsSetter != other.IsSetter) return false;
            if (!SequenceEqual(ParameterTypes, other.ParameterTypes)) return false;
            if (!SequenceEqual(GenericArguments, other.GenericArguments)) return false;
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as InvocationDescriptor);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (TargetType?.GetHashCode() ?? 0);
                hash = hash * 31 + MemberKind.GetHashCode();
                hash = hash * 31 + (MemberName?.GetHashCode() ?? 0);
                hash = hash * 31 + IsStatic.GetHashCode();
                hash = hash * 31 + IsSetter.GetHashCode();
                hash = hash * 31 + GetSequenceHashCode(ParameterTypes);
                hash = hash * 31 + GetSequenceHashCode(GenericArguments);
                return hash;
            }
        }

        private static bool SequenceEqual(Type[]? a, Type[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static int GetSequenceHashCode(Type[]? array)
        {
            if (array == null) return 0;
            int hash = 27;
            foreach (var item in array)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
