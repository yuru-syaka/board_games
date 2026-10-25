using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    /// <summary>
    /// あるプレイヤー視点の局面(不変)。自分の手札・所持金と公開情報だけを持つ
    /// (相手の手札は枚数だけ、相手の所持金は終局まで -1、伏せた入札額は競りが終わるまで隠す、山札は枚数だけ)。
    /// CPU と画面にはこれだけを渡す。
    /// </summary>
    public sealed class TrendPlayerView
    {
        /// <summary>見せる出来事の数(最近のものから)。通信で CPU の行動がまとめて届いても演出できる数にする。</summary>
        public const int RecentEventCount = 48;
        public const int HiddenMoney = -1;

        public int Viewer { get; }
        public int PlayerCount { get; }
        public IReadOnlyList<int> Hand { get; }
        public IReadOnlyList<int> HandCounts { get; }
        /// <summary>所持金。自分以外は終局まで -1。</summary>
        public IReadOnlyList<int> Money { get; }
        public IReadOnlyList<IReadOnlyList<int>> Bought { get; }
        public int DeckCount { get; }
        public int Round { get; }
        public IReadOnlyList<int> Counts { get; }
        public IReadOnlyList<IReadOnlyList<int>> Values { get; }
        public TrendPhase Phase { get; }
        public int CurrentPlayer { get; }
        public AuctionInfo Auction { get; }
        public int AuctionCount { get; }
        public int Ply { get; }
        /// <summary>最近の出来事(古い順)。</summary>
        public IReadOnlyList<TrendEvent> Events { get; }
        public IReadOnlyList<RoundSummary> Summaries { get; }
        public GameResult Result { get; }

        public TrendPlayerView(int viewer, int playerCount, IEnumerable<int> hand, IEnumerable<int> handCounts, IEnumerable<int> money,
            IEnumerable<IEnumerable<int>> bought, int deckCount, int round, IEnumerable<int> counts, IEnumerable<IEnumerable<int>> values,
            TrendPhase phase, int currentPlayer, AuctionInfo auction, int auctionCount, int ply, IEnumerable<TrendEvent> events,
            IEnumerable<RoundSummary> summaries, GameResult result)
        {
            Viewer = viewer;
            PlayerCount = playerCount;
            Hand = hand.ToArray();
            HandCounts = handCounts.ToArray();
            Money = money.ToArray();
            Bought = bought.Select(b => (IReadOnlyList<int>)b.ToArray()).ToArray();
            DeckCount = deckCount;
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
        }

        public static TrendPlayerView Create(TrendState s, int viewer)
        {
            if (viewer < 0 || viewer >= s.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            bool over = s.Result.IsOver;
            var money = s.Money.Select((m, p) => over || p == viewer ? m : HiddenMoney);
            bool sealedOpen = s.Phase == TrendPhase.Auction && s.Auction != null && s.Auction.Type == AuctionType.Sealed;
            int first = Math.Max(0, s.Events.Count - RecentEventCount);
            var events = new List<TrendEvent>(s.Events.Count - first);
            for (int i = first; i < s.Events.Count; i++)
            {
                var e = s.Events[i];
                // 進行中の入札の額は、本人以外には見せない
                if (e.Kind == EventKind.SealedBid && sealedOpen && e.AuctionNo == s.Auction.Number && e.Player != viewer)
                    e = e.WithAmount(-1);
                events.Add(e);
            }
            return new TrendPlayerView(viewer, s.PlayerCount, s.Hands[viewer], s.Hands.Select(h => h.Count), money, s.Bought,
                s.Deck.Count, s.Round, s.Counts, s.Values, s.Phase, s.CurrentPlayer, s.Auction?.ForViewer(viewer), s.AuctionCount, s.Ply,
                events, s.Summaries, s.Result);
        }

        public int MyMoney => Money[Viewer];
        public bool IsMyTurn => !Result.IsOver && CurrentPlayer == Viewer;

        /// <summary>自分の番の合法手(自分の番でなければ空)。</summary>
        public List<TrendMove> GetLegalMoves() =>
            IsMyTurn ? TrendMoves.LegalMoves(Phase, Auction, Hand, MyMoney) : new List<TrendMove>();

        public bool IsLegal(TrendMove move) =>
            IsMyTurn && (move.Card < 0 || TrendCards.IsValid(move.Card)) && TrendMoves.IsLegal(Phase, Auction, Hand, MyMoney, move);

        public int CumulativeValue(int artist, int throughRound) => TrendMoves.CumulativeValue(Values, artist, throughRound);

        /// <summary>最新の出来事の通し番号(出来事が無ければ -1)。</summary>
        public int LastEventSeq => Events.Count == 0 ? -1 : Events[Events.Count - 1].Seq;
    }
}
