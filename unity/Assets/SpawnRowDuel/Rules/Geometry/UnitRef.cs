using System;

namespace SpawnRowDuel.Rules
{
    public enum UnitRefKind : byte { None = 0, Cell = 1, Pool = 2 }

    /// <summary>
    /// Discriminated union over CellRef | PoolRef, plus the unit id for identity validation.
    /// Replaces the JS's duck-typed {key,i,c} vs {key,c} blocker refs (spec 01 s10, spec 03 s4.2).
    ///
    /// UnitId is the structural fix for the stale-reference class of bug: a declaration resolves as
    /// "the unit with this id, currently at this coordinate", so a mismatch is detectable and
    /// testable rather than silently targeting whoever moved into the cell.
    /// </summary>
    public readonly struct UnitRef : IEquatable<UnitRef>
    {
        public readonly UnitRefKind Kind;
        public readonly int UnitId;   // 0 == unknown; ALWAYS set when the ref names a live unit
        private readonly byte _a, _b, _c;

        private UnitRef(UnitRefKind kind, int unitId, byte a, byte b, byte c)
        {
            Kind = kind; UnitId = unitId; _a = a; _b = b; _c = c;
        }

        public static readonly UnitRef None = new UnitRef(UnitRefKind.None, 0, 0, 0, 0);

        public static UnitRef Cell(CellRef c, int unitId)
        {
            return new UnitRef(UnitRefKind.Cell, unitId, (byte)c.Row, c.Col, 0);
        }

        public static UnitRef Pool(PoolRef p, int unitId)
        {
            return new UnitRef(UnitRefKind.Pool, unitId, (byte)p.Owner, (byte)p.Zone, p.Index);
        }

        public bool IsCell { get { return Kind == UnitRefKind.Cell; } }
        public bool IsPool { get { return Kind == UnitRefKind.Pool; } }

        public CellRef AsCell
        {
            get { Require(UnitRefKind.Cell); return new CellRef((RowKey)_a, _b); }
        }

        public PoolRef AsPool
        {
            get { Require(UnitRefKind.Pool); return new PoolRef((Side)_a, (WorkerZone)_b, _c); }
        }

        private void Require(UnitRefKind k)
        {
            if (Kind != k)
                throw new InvalidOperationException("UnitRef is " + Kind + ", not " + k);
        }

        public bool Equals(UnitRef o)
        {
            return Kind == o.Kind && UnitId == o.UnitId && _a == o._a && _b == o._b && _c == o._c;
        }
        public override bool Equals(object obj) { return obj is UnitRef o && Equals(o); }
        public override int GetHashCode()
        {
            int h = (int)Kind;
            h = (h * 397) ^ UnitId; h = (h * 397) ^ _a; h = (h * 397) ^ _b; h = (h * 397) ^ _c;
            return h;
        }
        public static bool operator ==(UnitRef a, UnitRef b) { return a.Equals(b); }
        public static bool operator !=(UnitRef a, UnitRef b) { return !a.Equals(b); }

        public override string ToString()
        {
            switch (Kind)
            {
                case UnitRefKind.Cell: return "#" + UnitId + "@" + AsCell;
                case UnitRefKind.Pool: return "#" + UnitId + "@" + AsPool;
                default: return "UnitRef.None";
            }
        }
    }
}
