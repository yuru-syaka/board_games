using System;

namespace BoardGames.Quarto
{
    public enum MoveKind
    {
        /// <summary>渡された駒を (X, Y) に置く。</summary>
        Place,
        /// <summary>駒 Piece を相手に渡す。</summary>
        Give,
    }

    /// <summary>1 行動。Place のとき Piece は -1(置く駒は局面の手もちで決まる)、Give のとき X/Y は -1。</summary>
    public readonly struct QuartoMove : IEquatable<QuartoMove>
    {
        public readonly MoveKind Kind;
        public readonly int X;
        public readonly int Y;
        public readonly int Piece;

        QuartoMove(MoveKind kind, int x, int y, int piece)
        {
            Kind = kind;
            X = x;
            Y = y;
            Piece = piece;
        }

        public static QuartoMove Place(int x, int y) => new QuartoMove(MoveKind.Place, x, y, -1);

        public static QuartoMove Give(int piece) => new QuartoMove(MoveKind.Give, -1, -1, piece);

        public bool IsPlace => Kind == MoveKind.Place;
        public bool IsGive => Kind == MoveKind.Give;
        public int Cell => QuartoBoard.Index(X, Y);

        public bool Equals(QuartoMove other) =>
            Kind == other.Kind && X == other.X && Y == other.Y && Piece == other.Piece;

        public override bool Equals(object obj) => obj is QuartoMove other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, X, Y, Piece);

        public override string ToString() => IsPlace ? $"place ({X},{Y})" : $"give #{Piece}";
    }
}
