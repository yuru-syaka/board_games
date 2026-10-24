using System;

namespace BoardGames.GhostReversa
{
    /// <summary>駒の種類。Unknown は「相手の裏向きの駒」など、見えていない種類。</summary>
    public enum TileKind
    {
        Unknown = 0,
        /// <summary>青おばけ: 表で置くと同じ行(横)を裏返す。</summary>
        Blue = 1,
        /// <summary>赤おばけ: 表で置くと同じ列(縦)を裏返す。</summary>
        Red = 2,
        /// <summary>共有の特殊駒: 表で置くと盤上の全駒を裏返す。裏になったら盤外に戻る。</summary>
        Poltergeist = 3,
    }

    /// <summary>盤上の駒 1 つ(不変)。持ち主は表でも裏でも見える。ポルターガイストの持ち主は NoOwner。</summary>
    public readonly struct Tile : IEquatable<Tile>
    {
        public const int NoOwner = -1;

        public readonly int Owner;
        public readonly TileKind Kind;
        public readonly bool FaceUp;

        public Tile(int owner, TileKind kind, bool faceUp)
        {
            Owner = owner;
            Kind = kind;
            FaceUp = faceUp;
        }

        public static Tile Poltergeist => new Tile(NoOwner, TileKind.Poltergeist, true);

        public bool IsPoltergeist => Kind == TileKind.Poltergeist;

        public Tile Flipped() => new Tile(Owner, Kind, !FaceUp);

        public Tile WithKind(TileKind kind) => new Tile(Owner, kind, FaceUp);

        public bool Equals(Tile other) => Owner == other.Owner && Kind == other.Kind && FaceUp == other.FaceUp;
        public override bool Equals(object obj) => obj is Tile t && Equals(t);
        public override int GetHashCode() => (Owner + 1) * 31 + (int)Kind * 7 + (FaceUp ? 1 : 0);
        public override string ToString() => $"P{Owner}:{Kind}{(FaceUp ? "↑" : "↓")}";
    }
}
