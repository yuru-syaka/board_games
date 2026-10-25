using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    public enum TrendPhase
    {
        /// <summary>出品者がカードを選ぶ。</summary>
        Play,
        /// <summary>ダブルに添えるかを順に聞いている。</summary>
        DoubleOffer,
        /// <summary>競りの最中(公開・一巡・入札・指値の買う / 見送る)。</summary>
        Auction,
        /// <summary>指値の値段を出品者が決める。</summary>
        SetPrice,
        /// <summary>対局が終わった。</summary>
        GameOver,
    }

    /// <summary>
    /// 完全情報の対局状態(不変)。全員の手札・所持金と山札の並びを含むので、CPU や画面には直接渡さない
    /// (TrendPlayerView を使う)。
    /// </summary>
    public sealed class TrendState
    {
        public int PlayerCount { get; }
        public IReadOnlyList<int> Money { get; }
        public IReadOnlyList<IReadOnlyList<int>> Hands { get; }
        /// <summary>このラウンドに買った絵。</summary>
        public IReadOnlyList<IReadOnlyList<int>> Bought { get; }
        public IReadOnlyList<int> Deck { get; }
        /// <summary>今のラウンド(0..3)。終局後は 4。</summary>
        public int Round { get; }
        /// <summary>画家ごとの、このラウンドに出された枚数。</summary>
        public IReadOnlyList<int> Counts { get; }
        /// <summary>[ラウンド][画家] に足された価値(30 / 20 / 10 / 0)。</summary>
        public IReadOnlyList<IReadOnlyList<int>> Values { get; }
        public TrendPhase Phase { get; }
        /// <summary>手を指す人(終局後は -1)。</summary>
        public int CurrentPlayer { get; }
        /// <summary>進行中の競り(無ければ null)。</summary>
        public AuctionInfo Auction { get; }
        /// <summary>これまでに始まった競りの数。</summary>
        public int AuctionCount { get; }
        public int Ply { get; }
        public IReadOnlyList<TrendEvent> Events { get; }
        public IReadOnlyList<RoundSummary> Summaries { get; }
        public GameResult Result { get; }
        public TrendMove? LastMove { get; }

        public TrendState(int playerCount, IEnumerable<int> money, IEnumerable<IEnumerable<int>> hands,
            IEnumerable<IEnumerable<int>> bought, IEnumerable<int> deck, int round, IEnumerable<int> counts,
            IEnumerable<IEnumerable<int>> values, TrendPhase phase, int currentPlayer, AuctionInfo auction, int auctionCount,
            int ply, IEnumerable<TrendEvent> events, IEnumerable<RoundSummary> summaries, GameResult result, TrendMove? lastMove)
        {
            if (!TrendLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            PlayerCount = playerCount;
            Money = money.ToArray();
            Hands = hands.Select(h => (IReadOnlyList<int>)h.ToArray()).ToArray();
            Bought = bought.Select(b => (IReadOnlyList<int>)b.ToArray()).ToArray();
            Deck = deck.ToArray();
            Round = round;
            Counts = counts.ToArray();
            Values = values.Select(v => (IReadOnlyList<int>)v.ToArray()).ToArray();
            Phase = phase;
            CurrentPlayer = currentPlayer;
            Auction = auction;
            AuctionCount = auctionCount;
            Ply = ply;
            Events = events.ToArray();
            Summaries = summaries.ToArray();
            Result = result;
            LastMove = lastMove;
            if (Money.Count != playerCount || Hands.Count != playerCount || Bought.Count != playerCount)
                throw new ArgumentException("player arrays must match playerCount");
            if (Counts.Count != TrendCards.ArtistCount || Values.Count != TrendLogic.Rounds)
                throw new ArgumentException("bad counts / values");
        }

        /// <summary>その画家の、round までのラウンドで足された価値の合計。</summary>
        public int CumulativeValue(int artist, int throughRound) => TrendMoves.CumulativeValue(Values, artist, throughRound);
    }

    /// <summary>手の合法性と一覧。完全な局面(TrendState)とプレイヤー視点(TrendPlayerView)で共通に使う。</summary>
    public static class TrendMoves
    {
        public static int CumulativeValue(IReadOnlyList<IReadOnlyList<int>> values, int artist, int throughRound)
        {
            int sum = 0;
            for (int r = 0; r <= throughRound && r < values.Count; r++) sum += values[r][artist];
            return sum;
        }

        /// <summary>手番の人(hand / money はその人のもの)にとって move が合法か。</summary>
        public static bool IsLegal(TrendPhase phase, AuctionInfo auction, IReadOnlyList<int> hand, int money, TrendMove move)
        {
            switch (move.Kind)
            {
                case MoveKind.Play:
                    return phase == TrendPhase.Play && Contains(hand, move.Card);
                case MoveKind.AddDouble:
                    return phase == TrendPhase.DoubleOffer && auction != null && Contains(hand, move.Card) &&
                           TrendCards.ArtistOf(move.Card) == auction.Artist && TrendCards.TypeOf(move.Card) != AuctionType.Double;
                case MoveKind.Decline:
                    return phase == TrendPhase.DoubleOffer;
                case MoveKind.Bid:
                    if (phase != TrendPhase.Auction || auction == null || move.Amount > money) return false;
                    if (auction.Type == AuctionType.Sealed) return move.Amount >= 0;
                    return (auction.Type == AuctionType.Open || auction.Type == AuctionType.OnceAround) &&
                           move.Amount > auction.HighBid && move.Amount >= 1;
                case MoveKind.Pass:
                    return phase == TrendPhase.Auction && auction != null &&
                           (auction.Type == AuctionType.Open || auction.Type == AuctionType.OnceAround || auction.Type == AuctionType.Fixed);
                case MoveKind.SetPrice:
                    return phase == TrendPhase.SetPrice && move.Amount >= 0 && move.Amount <= money;
                case MoveKind.Accept:
                    return phase == TrendPhase.Auction && auction != null && auction.Type == AuctionType.Fixed && auction.Price <= money;
                default:
                    return false;
            }
        }

        /// <summary>手番の人の合法手の一覧(値を付ける手は、付けられる額をすべて並べる)。</summary>
        public static List<TrendMove> LegalMoves(TrendPhase phase, AuctionInfo auction, IReadOnlyList<int> hand, int money)
        {
            var moves = new List<TrendMove>();
            switch (phase)
            {
                case TrendPhase.Play:
                    foreach (var c in hand) moves.Add(TrendMove.Play(c));
                    break;
                case TrendPhase.DoubleOffer:
                    foreach (var c in hand)
                    {
                        var m = TrendMove.AddDouble(c);
                        if (IsLegal(phase, auction, hand, money, m)) moves.Add(m);
                    }
                    moves.Add(TrendMove.Decline());
                    break;
                case TrendPhase.SetPrice:
                    for (int a = 0; a <= money; a++) moves.Add(TrendMove.SetPrice(a));
                    break;
                case TrendPhase.Auction:
                    if (auction == null) break;
                    switch (auction.Type)
                    {
                        case AuctionType.Sealed:
                            for (int a = 0; a <= money; a++) moves.Add(TrendMove.Bid(a));
                            break;
                        case AuctionType.Fixed:
                            if (auction.Price <= money) moves.Add(TrendMove.Accept());
                            moves.Add(TrendMove.Pass());
                            break;
                        default:
                            for (int a = Math.Max(1, auction.HighBid + 1); a <= money; a++) moves.Add(TrendMove.Bid(a));
                            moves.Add(TrendMove.Pass());
                            break;
                    }
                    break;
            }
            return moves;
        }

        /// <summary>今付けられる最低額(公開・一巡は今の最高額 +1、入札・指値の値段は 0)。</summary>
        public static int MinAmount(TrendPhase phase, AuctionInfo auction)
        {
            if (phase == TrendPhase.Auction && auction != null &&
                (auction.Type == AuctionType.Open || auction.Type == AuctionType.OnceAround))
                return Math.Max(1, auction.HighBid + 1);
            return 0;
        }

        static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
