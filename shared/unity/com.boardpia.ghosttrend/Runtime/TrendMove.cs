using System;

namespace BoardGames.GhostTrend
{
    public enum MoveKind
    {
        /// <summary>手札から 1 枚出品する(Card)。</summary>
        Play,
        /// <summary>ダブルに同じ画家のカードを添える(Card)。</summary>
        AddDouble,
        /// <summary>ダブルに添えない。</summary>
        Decline,
        /// <summary>値を付ける(Amount)。入札では伏せた額。</summary>
        Bid,
        /// <summary>降りる(公開・一巡)/ 見送る(指値)。</summary>
        Pass,
        /// <summary>指値の値段を決める(Amount)。</summary>
        SetPrice,
        /// <summary>指値で買う。</summary>
        Accept,
    }

    /// <summary>1 手。種類ごとに Card / Amount のどちらかを使う(使わないほうは -1 / 0)。</summary>
    public readonly struct TrendMove : IEquatable<TrendMove>
    {
        public readonly MoveKind Kind;
        public readonly int Card;
        public readonly int Amount;

        TrendMove(MoveKind kind, int card, int amount)
        {
            Kind = kind;
            Card = card;
            Amount = amount;
        }

        public static TrendMove Play(int card) => new TrendMove(MoveKind.Play, card, 0);
        public static TrendMove AddDouble(int card) => new TrendMove(MoveKind.AddDouble, card, 0);
        public static TrendMove Decline() => new TrendMove(MoveKind.Decline, -1, 0);
        public static TrendMove Bid(int amount) => new TrendMove(MoveKind.Bid, -1, amount);
        public static TrendMove Pass() => new TrendMove(MoveKind.Pass, -1, 0);
        public static TrendMove SetPrice(int amount) => new TrendMove(MoveKind.SetPrice, -1, amount);
        public static TrendMove Accept() => new TrendMove(MoveKind.Accept, -1, 0);

        /// <summary>復元用(通信)。種類に合わない値は正規化する。</summary>
        public static TrendMove Create(MoveKind kind, int card, int amount)
        {
            switch (kind)
            {
                case MoveKind.Play: return Play(card);
                case MoveKind.AddDouble: return AddDouble(card);
                case MoveKind.Bid: return Bid(amount);
                case MoveKind.SetPrice: return SetPrice(amount);
                case MoveKind.Decline: return Decline();
                case MoveKind.Pass: return Pass();
                case MoveKind.Accept: return Accept();
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public bool Equals(TrendMove other) => Kind == other.Kind && Card == other.Card && Amount == other.Amount;
        public override bool Equals(object obj) => obj is TrendMove m && Equals(m);
        public override int GetHashCode() => ((int)Kind * 397 + Card) * 397 + Amount;
        public static bool operator ==(TrendMove a, TrendMove b) => a.Equals(b);
        public static bool operator !=(TrendMove a, TrendMove b) => !a.Equals(b);

        public override string ToString()
        {
            switch (Kind)
            {
                case MoveKind.Play:
                case MoveKind.AddDouble:
                    return $"{Kind}({Card})";
                case MoveKind.Bid:
                case MoveKind.SetPrice:
                    return $"{Kind}({Amount})";
                default:
                    return Kind.ToString();
            }
        }
    }
}
