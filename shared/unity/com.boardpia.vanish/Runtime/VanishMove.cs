using System;

namespace BoardGames.Vanish
{
    /// <summary>1 手 = 空いているマス Cell に自分の駒を置く。</summary>
    public readonly struct VanishMove : IEquatable<VanishMove>
    {
        public readonly int Cell;

        public VanishMove(int cell)
        {
            Cell = cell;
        }

        public static VanishMove Place(int cell) => new VanishMove(cell);

        public bool Equals(VanishMove other) => Cell == other.Cell;
        public override bool Equals(object obj) => obj is VanishMove m && Equals(m);
        public override int GetHashCode() => Cell;
        public override string ToString() => $"Place({Cell})";
    }
}
