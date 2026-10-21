using System;

namespace BoardGames.Gobblet
{
    /// <summary>
    /// 1 手。From == FromHand なら手持ちから Size の駒を置く。それ以外は From のマスの一番上の駒(大きさ Size)を To へ動かす。
    /// </summary>
    public readonly struct GobbletMove : IEquatable<GobbletMove>
    {
        public const int FromHand = -1;

        public readonly int From;
        public readonly PieceSize Size;
        public readonly int To;

        public GobbletMove(int from, PieceSize size, int to)
        {
            From = from;
            Size = size;
            To = to;
        }

        public static GobbletMove Place(PieceSize size, int to) => new GobbletMove(FromHand, size, to);
        public static GobbletMove Move(int from, PieceSize size, int to) => new GobbletMove(from, size, to);

        public bool IsFromHand => From == FromHand;

        public bool Equals(GobbletMove other) => From == other.From && Size == other.Size && To == other.To;
        public override bool Equals(object obj) => obj is GobbletMove m && Equals(m);
        public override int GetHashCode() => ((From + 1) * 3 + (int)Size) * 9 + To;
        public override string ToString() => IsFromHand ? $"Place({Size}->{To})" : $"Move({From}:{Size}->{To})";
    }
}
