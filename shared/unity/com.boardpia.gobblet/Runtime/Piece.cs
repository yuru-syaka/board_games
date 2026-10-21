using System;

namespace BoardGames.Gobblet
{
    /// <summary>駒の大きさ。大きい駒は小さい駒にかぶせられる。</summary>
    public enum PieceSize
    {
        Small = 0,
        Medium = 1,
        Large = 2,
    }

    /// <summary>
    /// 駒 1 体。同じ持ち主・同じ大きさの駒は区別しない(手持ちも大きさごとの数で管理する)。
    /// </summary>
    public readonly struct Piece : IEquatable<Piece>
    {
        public readonly int Owner;
        public readonly PieceSize Size;

        public Piece(int owner, PieceSize size)
        {
            Owner = owner;
            Size = size;
        }

        public bool Equals(Piece other) => Owner == other.Owner && Size == other.Size;
        public override bool Equals(object obj) => obj is Piece p && Equals(p);
        public override int GetHashCode() => Owner * 3 + (int)Size;
        public override string ToString() => $"P{Owner}{Size}";
    }
}
