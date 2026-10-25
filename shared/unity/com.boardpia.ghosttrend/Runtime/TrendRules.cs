using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    /// <summary>ゴースト・トレンドのルール。状態は不変で、ApplyMove は新しい状態を返す。</summary>
    public sealed class TrendRules : IGameRules<TrendState, TrendMove>
    {
        public IReadOnlyList<TrendMove> GetLegalMoves(TrendState s)
        {
            if (s.Result.IsOver || s.CurrentPlayer < 0) return Array.Empty<TrendMove>();
            return TrendMoves.LegalMoves(s.Phase, s.Auction, s.Hands[s.CurrentPlayer], s.Money[s.CurrentPlayer]);
        }

        public bool IsLegal(TrendState s, TrendMove move) =>
            !s.Result.IsOver && s.CurrentPlayer >= 0 &&
            (move.Card < 0 || TrendCards.IsValid(move.Card)) &&
            TrendMoves.IsLegal(s.Phase, s.Auction, s.Hands[s.CurrentPlayer], s.Money[s.CurrentPlayer], move);

        public GameResult GetResult(TrendState s) => s.Result;

        public TrendState ApplyMove(TrendState s, TrendMove move)
        {
            if (!IsLegal(s, move)) throw new InvalidOperationException($"Illegal move {move} in phase {s.Phase}");
            var w = TrendWork.From(s);
            int p = w.Current;
            w.Ply++;
            switch (move.Kind)
            {
                case MoveKind.Play: w.PlayCard(p, move.Card); break;
                case MoveKind.AddDouble: w.AddDouble(p, move.Card); break;
                case MoveKind.Decline: w.DeclineDouble(p); break;
                case MoveKind.Bid: w.Bid(p, move.Amount); break;
                case MoveKind.Pass: w.Pass(p); break;
                case MoveKind.SetPrice: w.SetPrice(p, move.Amount); break;
                case MoveKind.Accept: w.Accept(p); break;
            }
            return w.ToState(move);
        }
    }

    /// <summary>ルールの手順を、書き換えられる作業用の局面の上で進める(ApplyMove と初期化で使う)。</summary>
    internal sealed class TrendWork
    {
        public int N;
        public int[] Money;
        public List<int>[] Hands;
        public List<int>[] Bought;
        public List<int> Deck;
        public int Round;
        public int[] Counts;
        public int[][] Values;
        public TrendPhase Phase;
        public int Current;
        public AuctionInfo Auction;
        public int AuctionCount;
        public int Ply;
        public List<TrendEvent> Events;
        public List<RoundSummary> Summaries;
        public GameResult Result = GameResult.Ongoing;

        public static TrendWork From(TrendState s) => new TrendWork
        {
            N = s.PlayerCount,
            Money = s.Money.ToArray(),
            Hands = s.Hands.Select(h => h.ToList()).ToArray(),
            Bought = s.Bought.Select(b => b.ToList()).ToArray(),
            Deck = s.Deck.ToList(),
            Round = s.Round,
            Counts = s.Counts.ToArray(),
            Values = s.Values.Select(v => v.ToArray()).ToArray(),
            Phase = s.Phase,
            Current = s.CurrentPlayer,
            Auction = s.Auction,
            AuctionCount = s.AuctionCount,
            Ply = s.Ply,
            Events = s.Events.ToList(),
            Summaries = s.Summaries.ToList(),
            Result = s.Result,
        };

        /// <summary>配る前の空の局面(初期化用)。</summary>
        public static TrendWork Empty(int playerCount, IEnumerable<int> deck)
        {
            var w = new TrendWork
            {
                N = playerCount,
                Money = Enumerable.Repeat(TrendLogic.InitialMoney, playerCount).ToArray(),
                Hands = Enumerable.Range(0, playerCount).Select(_ => new List<int>()).ToArray(),
                Bought = Enumerable.Range(0, playerCount).Select(_ => new List<int>()).ToArray(),
                Deck = deck.ToList(),
                Counts = new int[TrendCards.ArtistCount],
                Values = Enumerable.Range(0, TrendLogic.Rounds).Select(_ => new int[TrendCards.ArtistCount]).ToArray(),
                Events = new List<TrendEvent>(),
                Summaries = new List<RoundSummary>(),
            };
            return w;
        }

        public TrendState ToState(TrendMove? lastMove) =>
            new TrendState(N, Money, Hands, Bought, Deck, Round, Counts, Values, Phase, Current, Auction, AuctionCount, Ply, Events,
                Summaries, Result, lastMove);

        void Log(EventKind kind, int player, int card = -1, int card2 = -1, int amount = 0, int other = -1) =>
            Events.Add(new TrendEvent(Events.Count, kind, player, card, card2, amount, other, AuctionCount));

        int Next(int p) => TrendLogic.Next(p, N);

        // ---------- ラウンド ----------

        /// <summary>配って、starter から(手札のある人を)出品者にする。</summary>
        public void StartRound(int starter)
        {
            int deal = TrendLogic.CardsToDeal(N, Round);
            for (int i = 0; i < deal; i++)
            for (int p = 0; p < N; p++)
            {
                if (Deck.Count == 0) break;
                Hands[p].Add(Deck[0]);
                Deck.RemoveAt(0);
            }
            for (int p = 0; p < N; p++) Hands[p].Sort();
            Array.Clear(Counts, 0, Counts.Length);
            Auction = null;
            Log(EventKind.RoundStart, -1, amount: Round);
            StartTurn(starter);
        }

        /// <summary>from から順に、手札のある人の出品にする。誰も手札が無ければラウンドを終える。</summary>
        void StartTurn(int from)
        {
            Auction = null;
            for (int i = 0; i < N; i++)
            {
                int p = (from + i) % N;
                if (Hands[p].Count == 0) continue;
                Phase = TrendPhase.Play;
                Current = p;
                return;
            }
            EndRound(from);
        }

        void EndRound(int nextStarter)
        {
            var ranks = TrendLogic.RankArtists(Counts);
            for (int a = 0; a < TrendCards.ArtistCount; a++)
                Values[Round][a] = ranks[a] >= 0 ? TrendLogic.RankValues[ranks[a]] : 0;

            var cardValues = new int[TrendCards.ArtistCount];
            for (int a = 0; a < cardValues.Length; a++)
                cardValues[a] = ranks[a] >= 0 ? TrendMoves.CumulativeValue(Values, a, Round) : 0;

            var payouts = new int[N];
            for (int p = 0; p < N; p++)
            {
                foreach (var c in Bought[p]) payouts[p] += cardValues[TrendCards.ArtistOf(c)];
                Money[p] += payouts[p];
                Bought[p].Clear();
            }
            Summaries.Add(new RoundSummary(Round, Counts, ranks, cardValues, payouts));
            Log(EventKind.RoundEnd, -1, amount: Round);
            Auction = null;
            Round++;

            if (Round >= TrendLogic.Rounds)
            {
                Phase = TrendPhase.GameOver;
                Current = -1;
                int best = Money.Max();
                var leaders = Enumerable.Range(0, N).Where(p => Money[p] == best).ToList();
                Result = leaders.Count == 1 ? GameResult.WinFor(leaders[0]) : GameResult.Draw;
                Log(EventKind.GameEnd, Result.Winner);
                return;
            }
            StartRound(nextStarter);
        }

        /// <summary>カードを場に出して数える。5 枚目ならラウンドを終えて true。</summary>
        bool CountCard(int card, int player)
        {
            int artist = TrendCards.ArtistOf(card);
            Counts[artist]++;
            if (Counts[artist] < TrendLogic.RoundEndCount) return false;
            EndRound(Next(player));
            return true;
        }

        // ---------- 出品 ----------

        public void PlayCard(int p, int card)
        {
            Hands[p].Remove(card);
            Log(EventKind.Played, p, card);
            if (CountCard(card, p)) return;

            var type = TrendCards.TypeOf(card);
            if (type == AuctionType.Double)
            {
                AuctionCount++;
                var order = Enumerable.Range(0, N).Select(i => (p + i) % N);
                Auction = new AuctionInfo(AuctionCount, p, p, AuctionType.Double, new[] { card }, 0, -1, -1,
                    new bool[N], Enumerable.Repeat(AuctionInfo.NotSubmitted, N), order, 0);
                Phase = TrendPhase.DoubleOffer;
                Current = p;
                return;
            }
            AuctionCount++;
            BeginAuction(p, p, new[] { card }, type);
        }

        public void AddDouble(int p, int card)
        {
            Hands[p].Remove(card);
            var first = Auction.Cards[0];
            Log(EventKind.DoubleAdded, p, card, first);
            if (CountCard(card, p)) return;
            BeginAuction(p, Auction.Originator, new[] { first, card }, TrendCards.TypeOf(card));
        }

        public void DeclineDouble(int p)
        {
            Log(EventKind.Declined, p);
            int step = Auction.Step + 1;
            if (step >= Auction.Order.Count)
            {
                // 誰も添えなかった: 最初の出品者が 0 で引き取る
                Sell(Auction.Originator, 0);
                return;
            }
            Auction = Auction.With(step: step);
            Current = Auction.Order[step];
        }

        void BeginAuction(int auctioneer, int originator, int[] cards, AuctionType type)
        {
            var passed = new bool[N];
            var bids = Enumerable.Repeat(AuctionInfo.NotSubmitted, N).ToArray();
            // 左隣から順に、出品者は最後
            var order = Enumerable.Range(1, N).Select(i => (auctioneer + i) % N).ToArray();
            Auction = new AuctionInfo(AuctionCount, auctioneer, originator, type, cards, 0, -1, -1, passed, bids, order, 0);
            if (type == AuctionType.Fixed)
            {
                Phase = TrendPhase.SetPrice;
                Current = auctioneer;
                return;
            }
            Phase = TrendPhase.Auction;
            Current = order[0];
        }

        // ---------- 競り ----------

        public void Bid(int p, int amount)
        {
            var a = Auction;
            switch (a.Type)
            {
                case AuctionType.Open:
                    Log(EventKind.Bid, p, amount: amount);
                    Auction = a.With(highBid: amount, highBidder: p);
                    AdvanceOpen(p);
                    break;
                case AuctionType.OnceAround:
                    Log(EventKind.Bid, p, amount: amount);
                    Auction = a.With(highBid: amount, highBidder: p);
                    AdvanceOrder();
                    break;
                case AuctionType.Sealed:
                    Log(EventKind.SealedBid, p, amount: amount);
                    var bids = a.Bids.ToArray();
                    bids[p] = amount;
                    Auction = a.With(bids: bids);
                    AdvanceOrder();
                    break;
            }
        }

        public void Pass(int p)
        {
            var a = Auction;
            Log(EventKind.Pass, p);
            if (a.Type == AuctionType.Open)
            {
                var passed = a.Passed.ToArray();
                passed[p] = true;
                Auction = a.With(passed: passed);
                AdvanceOpen(p);
            }
            else
            {
                AdvanceOrder();
            }
        }

        public void SetPrice(int p, int amount)
        {
            Log(EventKind.SetPrice, p, amount: amount);
            // 指値は出品者以外が左隣から順に答える
            var order = Auction.Order.Where(x => x != Auction.Auctioneer);
            Auction = Auction.With(price: amount, order: order, step: 0);
            Phase = TrendPhase.Auction;
            Current = Auction.Order[0];
        }

        public void Accept(int p)
        {
            Log(EventKind.Accept, p);
            Sell(p, Auction.Price);
        }

        /// <summary>公開競り: 降りていない次の人へ。最高額の人に戻った・全員降りたら落札。</summary>
        void AdvanceOpen(int from)
        {
            var a = Auction;
            for (int i = 1; i <= N; i++)
            {
                int q = (from + i) % N;
                if (a.Passed[q]) continue;
                if (q == a.HighBidder)
                {
                    Sell(q, a.HighBid);
                    return;
                }
                Current = q;
                return;
            }
            // 全員降りた
            if (a.HighBidder >= 0) Sell(a.HighBidder, a.HighBid);
            else Sell(a.Auctioneer, 0);
        }

        /// <summary>一巡・入札・指値: 順番の次の人へ。全員終わったら決着。</summary>
        void AdvanceOrder()
        {
            var a = Auction;
            int step = a.Step + 1;
            if (step < a.Order.Count)
            {
                Auction = a.With(step: step);
                Current = a.Order[step];
                return;
            }
            switch (a.Type)
            {
                case AuctionType.OnceAround:
                    if (a.HighBidder >= 0) Sell(a.HighBidder, a.HighBid);
                    else Sell(a.Auctioneer, 0);
                    break;
                case AuctionType.Sealed:
                    // 最高額。同額なら出品者 → 左隣 → … の順で近い人
                    int winner = -1, best = -1;
                    for (int i = 0; i < N; i++)
                    {
                        int q = (a.Auctioneer + i) % N;
                        if (a.Bids[q] > best)
                        {
                            best = a.Bids[q];
                            winner = q;
                        }
                    }
                    Sell(winner, Math.Max(0, best));
                    break;
                case AuctionType.Fixed:
                    Sell(a.Auctioneer, a.Price); // 誰も買わなかった: 出品者が買う
                    break;
            }
        }

        void Sell(int buyer, int price)
        {
            var a = Auction;
            int seller = a.Auctioneer;
            if (buyer != seller) Money[seller] += price;
            Money[buyer] -= price;
            Bought[buyer].AddRange(a.Cards);
            Log(EventKind.Sold, buyer, a.Cards[0], a.Cards.Count > 1 ? a.Cards[1] : -1, price, seller);
            StartTurn(Next(seller));
        }
    }
}
