
using System;

namespace Modules.ComponentData.Skeleton
{
    public struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Default = new();
        public bool Equals(Unit other) => true;
        public override bool Equals(object obj) => obj is Unit;
        public override int GetHashCode() => 1234567;
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
