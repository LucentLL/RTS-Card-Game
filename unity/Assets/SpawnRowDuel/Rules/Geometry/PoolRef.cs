using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// A worker-pool slot. Workers live in a per-zone pool, NOT in board cells
    /// (spec 01 s5). Mirrors the MP wire shape {po, pw, pi}.
    /// </summary>
    public readonly struct PoolRef : IEquatable<PoolRef>
    {
        public readonly Side Owner;
        public readonly WorkerZone Zone;
        public readonly byte Index;

        public PoolRef(Side owner, WorkerZone zone, byte index) { Owner = owner; Zone = zone; Index = index; }

        public bool Equals(PoolRef o) { return Owner == o.Owner && Zone == o.Zone && Index == o.Index; }
        public override bool Equals(object obj) { return obj is PoolRef o && Equals(o); }
        public override int GetHashCode() { return (((int)Owner * 397) ^ (int)Zone) * 397 ^ Index; }
        public static bool operator ==(PoolRef a, PoolRef b) { return a.Equals(b); }
        public static bool operator !=(PoolRef a, PoolRef b) { return !a.Equals(b); }
        public override string ToString() { return Owner + "." + Zone + "[" + Index + "]"; }
    }
}
