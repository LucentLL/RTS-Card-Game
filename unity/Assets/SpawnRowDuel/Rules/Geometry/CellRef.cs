using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>A board cell. Col is 0..6. Value type, cheap to copy, safe as a dictionary key.</summary>
    public readonly struct CellRef : IEquatable<CellRef>
    {
        public readonly RowKey Row;
        public readonly byte Col;

        public CellRef(RowKey row, byte col) { Row = row; Col = col; }
        public CellRef(RowKey row, int col) { Row = row; Col = (byte)col; }

        public int Index { get { return (int)Row * Board.Columns + Col; } }

        public static CellRef FromIndex(int i)
        {
            return new CellRef((RowKey)(i / Board.Columns), (byte)(i % Board.Columns));
        }

        public bool Equals(CellRef other) { return Row == other.Row && Col == other.Col; }
        public override bool Equals(object obj) { return obj is CellRef o && Equals(o); }
        public override int GetHashCode() { return ((int)Row * 397) ^ Col; }
        public static bool operator ==(CellRef a, CellRef b) { return a.Equals(b); }
        public static bool operator !=(CellRef a, CellRef b) { return !a.Equals(b); }
        public override string ToString() { return Row + "[" + Col + "]"; }
    }
}
