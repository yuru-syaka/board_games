using System;

namespace BoardGames.GhostReversa
{
    public enum MoveKind
    {
        /// <summary>準備: 手駒を 1 体伏せて捨てる(Piece = 捨てる色)。</summary>
        Discard = 0,
        /// <summary>駒(手駒の青・赤、またはポルターガイスト)を (X, Y) に置く。</summary>
        Place = 1,
        /// <summary>置ける手がないときのパス。</summary>
        Pass = 2,
    }

    /// <summary>1 手(不変)。同じ手は必ず同じ値になるよう、作るときに使わない項目をそろえる。</summary>
    public readonly struct ReversaMove : IEquatable<ReversaMove>
    {
        public readonly MoveKind Kind;
        public readonly TileKind Piece;
        public readonly int X;
        public readonly int Y;
        public readonly bool FaceUp;

        ReversaMove(MoveKind kind, TileKind piece, int x, int y, bool faceUp)
        {
            Kind = kind;
            Piece = piece;
            X = x;
            Y = y;
            FaceUp = faceUp;
        }

        public static ReversaMove Discard(TileKind color) => new ReversaMove(MoveKind.Discard, color, -1, -1, false);

        public static ReversaMove Place(TileKind piece, int x, int y, bool faceUp) =>
            new ReversaMove(MoveKind.Place, piece, x, y, piece == TileKind.Poltergeist || faceUp);

        public static ReversaMove Pass() => new ReversaMove(MoveKind.Pass, TileKind.Unknown, -1, -1, false);

        /// <summary>種類(色)を伏せた手(相手の裏置き・捨てた色を見せないため)。</summary>
        public ReversaMove Hidden() => new ReversaMove(Kind, TileKind.Unknown, X, Y, FaceUp);

        public bool Equals(ReversaMove o) => Kind == o.Kind && Piece == o.Piece && X == o.X && Y == o.Y && FaceUp == o.FaceUp;
        public override bool Equals(object obj) => obj is ReversaMove m && Equals(m);
        public override int GetHashCode() => ((((int)Kind * 7 + (int)Piece) * 5 + X + 1) * 5 + Y + 1) * 2 + (FaceUp ? 1 : 0);

        public override string ToString()
        {
            switch (Kind)
            {
                case MoveKind.Discard: return $"Discard({Piece})";
                case MoveKind.Pass: return "Pass";
                default: return $"Place({Piece},{X},{Y},{(FaceUp ? "up" : "down")})";
            }
        }
    }
}
