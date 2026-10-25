using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostTrend.Tests
{
    public class TrendRulesTests
    {
        readonly TrendRules _rules = new TrendRules();

        static int C(int artist, AuctionType type, int nth = 0) => TrendCards.Find(artist, type, nth);

        /// <summary>3 人の局面。p0 の手札を指定し、ほかの 2 人には適当なカードを持たせる。</summary>
        static TrendState Three(int[] hand0, int[] hand1 = null, int[] hand2 = null, int[] money = null, int current = 0,
            int[] counts = null, int[][] values = null, int round = 0, int[][] bought = null)
        {
            hand1 = hand1 ?? new[] { C(4, AuctionType.Open, 1) };
            hand2 = hand2 ?? new[] { C(4, AuctionType.Open, 2) };
            return TrendSetup.CreateCustom(new[] { hand0, hand1, hand2 }, money, current, round, counts, values, bought);
        }

        TrendState Apply(TrendState s, params TrendMove[] moves)
        {
            foreach (var m in moves) s = _rules.ApplyMove(s, m);
            return s;
        }

        // ---------- カードと準備 ----------

        [Test]
        public void Cards_Has70CardsWithIncreasingCounts()
        {
            Assert.AreEqual(70, TrendCards.Count);
            CollectionAssert.AreEqual(new[] { 12, 13, 14, 15, 16 }, Enumerable.Range(0, 5).Select(TrendCards.CountOf));
            for (int a = 0; a < 5; a++)
                Assert.IsTrue(TrendCards.AllCards().Any(c => TrendCards.ArtistOf(c) == a && TrendCards.TypeOf(c) == AuctionType.Double));
        }

        [TestCase(3, 10, 6, 6, 0)]
        [TestCase(4, 9, 4, 4, 0)]
        [TestCase(5, 8, 3, 3, 0)]
        public void DealTable_MatchesPlayerCount(int n, int r0, int r1, int r2, int r3)
        {
            CollectionAssert.AreEqual(new[] { r0, r1, r2, r3 }, Enumerable.Range(0, 4).Select(r => TrendLogic.CardsToDeal(n, r)));
            Assert.LessOrEqual(n * (r0 + r1 + r2 + r3), TrendCards.Count, "山札が足りる");
        }

        [Test]
        public void Setup_DealsFirstRoundAndStartsWithFirstPlayer()
        {
            var s = TrendSetup.Create(TrendCards.AllCards(), 4, 2);
            Assert.AreEqual(4, s.PlayerCount);
            Assert.IsTrue(s.Hands.All(h => h.Count == 9));
            Assert.AreEqual(70 - 36, s.Deck.Count);
            Assert.AreEqual(2, s.CurrentPlayer);
            Assert.AreEqual(TrendPhase.Play, s.Phase);
            Assert.IsTrue(s.Money.All(m => m == 100));
            Assert.AreEqual(EventKind.RoundStart, s.Events[0].Kind);
            Assert.IsFalse(s.Result.IsOver);
        }

        [Test]
        public void CreateRandom_IsShuffledAndValid()
        {
            var s = TrendSetup.CreateRandom(new Random(5), 3);
            CollectionAssert.AreEquivalent(TrendCards.AllCards(), s.Hands.SelectMany(h => h).Concat(s.Deck));
            Assert.IsTrue(s.Hands.All(h => h.Count == 10));
        }

        [Test]
        public void RankArtists_TiesGoToLowerNumber_AndZeroIsUnranked()
        {
            var ranks = TrendLogic.RankArtists(new[] { 2, 2, 0, 1, 3 });
            CollectionAssert.AreEqual(new[] { 1, 2, -1, -1, 0 }, ranks);
            CollectionAssert.AreEqual(new[] { 0, -1, -1, -1, -1 }, TrendLogic.RankArtists(new[] { 1, 0, 0, 0, 0 }));
        }

        // ---------- 公開競り ----------

        [Test]
        public void Open_AuctioneerWinsOwnAuction_PaysBank()
        {
            int card = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { card, C(1, AuctionType.Open) }), TrendMove.Play(card));
            Assert.AreEqual(TrendPhase.Auction, s.Phase);
            Assert.AreEqual(1, s.CurrentPlayer, "出品者の左隣から");
            s = Apply(s, TrendMove.Bid(10), TrendMove.Pass(), TrendMove.Bid(15), TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { 85, 100, 100 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[0]);
            Assert.AreEqual(1, s.CurrentPlayer, "次は出品者の左隣");
            Assert.AreEqual(TrendPhase.Play, s.Phase);
            var sold = s.Events.Last(e => e.Kind == EventKind.Sold);
            Assert.AreEqual(0, sold.Player);
            Assert.AreEqual(15, sold.Amount);
        }

        [Test]
        public void Open_OtherPlayerWins_PaysAuctioneer()
        {
            int card = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { card, C(1, AuctionType.Open) }), TrendMove.Play(card),
                TrendMove.Bid(10), TrendMove.Pass(), TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { 110, 90, 100 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[1]);
        }

        [Test]
        public void Open_BiddingWarContinuesAroundUntilHighBidderComesBack()
        {
            int card = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { card, C(1, AuctionType.Open) }), TrendMove.Play(card),
                TrendMove.Bid(5), TrendMove.Bid(6), TrendMove.Pass(), TrendMove.Bid(7));
            Assert.AreEqual(2, s.CurrentPlayer, "p0 は降りたので p1 の次は p2");
            s = Apply(s, TrendMove.Pass());
            Assert.AreEqual(93, s.Money[1], "p1 が 7 で落札");
            Assert.AreEqual(107, s.Money[0]);
        }

        [Test]
        public void Open_EveryonePasses_AuctioneerGetsItFree()
        {
            int card = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { card, C(1, AuctionType.Open) }), TrendMove.Play(card),
                TrendMove.Pass(), TrendMove.Pass(), TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { 100, 100, 100 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[0]);
        }

        [Test]
        public void Bid_MustExceedHighBidAndNotExceedMoney()
        {
            int card = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { card, C(1, AuctionType.Open) }, money: new[] { 100, 100, 20 }), TrendMove.Play(card),
                TrendMove.Bid(10));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.Bid(10)), "同額は不可");
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.Bid(21)), "所持金を超えられない");
            Assert.IsTrue(_rules.IsLegal(s, TrendMove.Bid(20)));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.Accept()));
            Assert.Throws<InvalidOperationException>(() => _rules.ApplyMove(s, TrendMove.Bid(21)));
        }

        // ---------- 一巡競り ----------

        [Test]
        public void OnceAround_EachPlayerOnce_AuctioneerLast()
        {
            int card = C(1, AuctionType.OnceAround);
            var s = Apply(Three(new[] { card, C(2, AuctionType.Open) }), TrendMove.Play(card), TrendMove.Bid(5), TrendMove.Bid(8));
            Assert.AreEqual(0, s.CurrentPlayer, "出品者は最後");
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.Bid(8)));
            s = Apply(s, TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { 108, 100, 92 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[2]);
        }

        [Test]
        public void OnceAround_AllPass_AuctioneerGetsItFree()
        {
            int card = C(1, AuctionType.OnceAround);
            var s = Apply(Three(new[] { card, C(2, AuctionType.Open) }), TrendMove.Play(card),
                TrendMove.Pass(), TrendMove.Pass(), TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { card }, s.Bought[0]);
            CollectionAssert.AreEqual(new[] { 100, 100, 100 }, s.Money);
        }

        // ---------- 入札 ----------

        [Test]
        public void Sealed_HighestWins_TieGoesToClosestToAuctioneer()
        {
            int card = C(2, AuctionType.Sealed);
            var s = Apply(Three(new[] { card, C(3, AuctionType.Open) }), TrendMove.Play(card), TrendMove.Bid(10), TrendMove.Bid(10));
            Assert.IsTrue(_rules.IsLegal(s, TrendMove.Bid(0)), "入札は 0 でもよい");
            s = Apply(s, TrendMove.Bid(5));
            CollectionAssert.AreEqual(new[] { 110, 90, 100 }, s.Money, "同額の p1 と p2 では、出品者の左隣の p1");
            CollectionAssert.AreEqual(new[] { card }, s.Bought[1]);
        }

        [Test]
        public void Sealed_AuctioneerWinsTies()
        {
            int card = C(2, AuctionType.Sealed);
            var s = Apply(Three(new[] { card, C(3, AuctionType.Open) }), TrendMove.Play(card),
                TrendMove.Bid(7), TrendMove.Bid(3), TrendMove.Bid(7));
            CollectionAssert.AreEqual(new[] { 93, 100, 100 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[0]);
        }

        [Test]
        public void Sealed_BidsAreHiddenFromOthersUntilResolved()
        {
            int card = C(2, AuctionType.Sealed);
            var s = Apply(Three(new[] { card, C(3, AuctionType.Open) }), TrendMove.Play(card), TrendMove.Bid(42));
            var v2 = TrendPlayerView.Create(s, 2);
            Assert.AreEqual(AuctionInfo.Hidden, v2.Auction.Bids[1]);
            Assert.AreEqual(-1, v2.Events.Last(e => e.Kind == EventKind.SealedBid).Amount);
            var v1 = TrendPlayerView.Create(s, 1);
            Assert.AreEqual(42, v1.Auction.Bids[1], "自分の額は見える");

            s = Apply(s, TrendMove.Bid(1), TrendMove.Bid(2));
            var after = TrendPlayerView.Create(s, 2);
            Assert.AreEqual(42, after.Events.First(e => e.Kind == EventKind.SealedBid && e.Player == 1).Amount, "終わったら公開");
        }

        // ---------- 指値 ----------

        [Test]
        public void Fixed_FirstToAcceptBuys()
        {
            int card = C(3, AuctionType.Fixed);
            var s = Apply(Three(new[] { card, C(0, AuctionType.Open) }), TrendMove.Play(card));
            Assert.AreEqual(TrendPhase.SetPrice, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            s = Apply(s, TrendMove.SetPrice(20));
            Assert.AreEqual(1, s.CurrentPlayer);
            s = Apply(s, TrendMove.Pass(), TrendMove.Accept());
            CollectionAssert.AreEqual(new[] { 120, 100, 80 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[2]);
        }

        [Test]
        public void Fixed_NobodyAccepts_AuctioneerBuysFromBank()
        {
            int card = C(3, AuctionType.Fixed);
            var s = Apply(Three(new[] { card, C(0, AuctionType.Open) }), TrendMove.Play(card), TrendMove.SetPrice(20),
                TrendMove.Pass(), TrendMove.Pass());
            CollectionAssert.AreEqual(new[] { 80, 100, 100 }, s.Money);
            CollectionAssert.AreEqual(new[] { card }, s.Bought[0]);
        }

        [Test]
        public void Fixed_PriceLimitedByOwnMoney_AcceptLimitedByBuyerMoney()
        {
            int card = C(3, AuctionType.Fixed);
            var s = Apply(Three(new[] { card, C(0, AuctionType.Open) }, money: new[] { 30, 10, 100 }), TrendMove.Play(card));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.SetPrice(31)));
            s = Apply(s, TrendMove.SetPrice(30));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.Accept()), "p1 は 10 しか持っていない");
            Assert.IsTrue(_rules.IsLegal(s, TrendMove.Pass()));
        }

        // ---------- ダブル ----------

        [Test]
        public void Double_AuctioneerAddsSecondCard_UsesItsAuctionType()
        {
            int dbl = C(0, AuctionType.Double), open = C(0, AuctionType.Open);
            var s = Apply(Three(new[] { dbl, open, C(1, AuctionType.Open) }), TrendMove.Play(dbl));
            Assert.AreEqual(TrendPhase.DoubleOffer, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            s = Apply(s, TrendMove.AddDouble(open));
            Assert.AreEqual(TrendPhase.Auction, s.Phase);
            Assert.AreEqual(AuctionType.Open, s.Auction.Type);
            CollectionAssert.AreEqual(new[] { dbl, open }, s.Auction.Cards);
            Assert.AreEqual(2, s.Counts[0]);
            s = Apply(s, TrendMove.Bid(30), TrendMove.Pass(), TrendMove.Pass());
            CollectionAssert.AreEquivalent(new[] { dbl, open }, s.Bought[1]);
            CollectionAssert.AreEqual(new[] { 130, 70, 100 }, s.Money);
        }

        [Test]
        public void Double_AnotherPlayerAdds_BecomesAuctioneer()
        {
            int dbl = C(0, AuctionType.Double), fixedCard = C(0, AuctionType.Fixed);
            var s = Apply(Three(new[] { dbl, C(2, AuctionType.Open) }, new[] { fixedCard }), TrendMove.Play(dbl), TrendMove.Decline());
            Assert.AreEqual(1, s.CurrentPlayer);
            s = Apply(s, TrendMove.AddDouble(fixedCard));
            Assert.AreEqual(TrendPhase.SetPrice, s.Phase);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(1, s.Auction.Auctioneer);
            Assert.AreEqual(0, s.Auction.Originator);
            s = Apply(s, TrendMove.SetPrice(12), TrendMove.Accept()); // 左隣の p2 が買う
            CollectionAssert.AreEqual(new[] { 100, 112, 88 }, s.Money);
            Assert.AreEqual(2, s.CurrentPlayer, "次は新しい出品者 p1 の左隣");
        }

        [Test]
        public void Double_NobodyAdds_OriginatorGetsItFree()
        {
            int dbl = C(0, AuctionType.Double);
            var s = Apply(Three(new[] { dbl, C(2, AuctionType.Open) }), TrendMove.Play(dbl),
                TrendMove.Decline(), TrendMove.Decline(), TrendMove.Decline());
            CollectionAssert.AreEqual(new[] { dbl }, s.Bought[0]);
            CollectionAssert.AreEqual(new[] { 100, 100, 100 }, s.Money);
            Assert.AreEqual(1, s.CurrentPlayer);
        }

        [Test]
        public void Double_CannotAddDoubleOrOtherArtist()
        {
            int dbl = C(0, AuctionType.Double);
            var s = Apply(Three(new[] { dbl, C(0, AuctionType.Double, 1), C(1, AuctionType.Open) }), TrendMove.Play(dbl));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.AddDouble(C(0, AuctionType.Double, 1))));
            Assert.IsFalse(_rules.IsLegal(s, TrendMove.AddDouble(C(1, AuctionType.Open))));
            Assert.IsTrue(_rules.IsLegal(s, TrendMove.Decline()));
        }

        // ---------- ラウンドの終わり ----------

        [Test]
        public void FifthCard_EndsRoundWithoutAuction_AndPaysOut()
        {
            int fifth = C(0, AuctionType.Open);
            var bought = new[]
            {
                new[] { C(0, AuctionType.Sealed), C(0, AuctionType.Sealed, 1) },
                new[] { C(1, AuctionType.Sealed) },
                new[] { C(3, AuctionType.Sealed) },
            };
            var hands = new[] { new[] { fifth, C(2, AuctionType.Open) }, new[] { C(4, AuctionType.Open) }, new[] { C(4, AuctionType.Open, 1) } };
            var s = TrendSetup.CreateCustom(hands, null, 0, 0, new[] { 4, 3, 0, 1, 0 }, null, bought, TrendCards.AllCards());
            s = Apply(s, TrendMove.Play(fifth));

            Assert.AreEqual(1, s.Round, "次のラウンドへ");
            var sum = s.Summaries.Single();
            CollectionAssert.AreEqual(new[] { 5, 3, 0, 1, 0 }, sum.Counts, "5 枚目も数える");
            CollectionAssert.AreEqual(new[] { 0, 1, -1, 2, -1 }, sum.Ranks);
            CollectionAssert.AreEqual(new[] { 30, 20, 0, 10, 0 }, sum.CardValues);
            CollectionAssert.AreEqual(new[] { 60, 20, 10 }, sum.Payouts);
            CollectionAssert.AreEqual(new[] { 160, 120, 110 }, s.Money);
            Assert.IsTrue(s.Bought.All(b => b.Count == 0), "買った絵は捨てる");
            Assert.IsTrue(s.Counts.All(c => c == 0));
            Assert.AreEqual(1 + 6, s.Hands[1].Count, "2 ラウンド目は 6 枚配る");
            Assert.AreEqual(1, s.CurrentPlayer, "ラウンドを終えた人の左隣から");
            Assert.IsFalse(s.Events.Any(e => e.Kind == EventKind.Sold), "5 枚目は競りにかけない");
        }

        [Test]
        public void Payout_UsesCumulativeValues_OnlyIfRankedThisRound()
        {
            var values = new[]
            {
                new[] { 30, 20, 10, 0, 0 },
                new int[5], new int[5], new int[5],
            };
            var bought = new[]
            {
                new[] { C(0, AuctionType.Sealed) }, // ルミナ: 前のラウンドは 30 だが今回は圏外 → 0
                new[] { C(1, AuctionType.Sealed) }, // ヨミ: 20 + 今回 1 位の 30 = 50
                new[] { C(4, AuctionType.Sealed) }, // ネオ: 今回 2 位の 20
            };
            int fifth = C(1, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { fifth }, new[] { C(2, AuctionType.Open) }, new[] { C(2, AuctionType.Fixed) } },
                null, 0, 1, new[] { 0, 4, 1, 1, 3 }, values, bought);
            s = Apply(s, TrendMove.Play(fifth));
            var sum = s.Summaries.Single();
            CollectionAssert.AreEqual(new[] { 0, 50, 10 + 10, 0, 20 }, sum.CardValues);
            CollectionAssert.AreEqual(new[] { 0, 50, 20 }, sum.Payouts);
        }

        [Test]
        public void SecondCardOfDoubleBeingFifth_EndsRound()
        {
            int dbl = C(0, AuctionType.Double), open = C(0, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { dbl, open }, new[] { C(1, AuctionType.Open) }, new[] { C(2, AuctionType.Open) } },
                null, 0, 0, new[] { 3, 0, 0, 0, 0 });
            s = Apply(s, TrendMove.Play(dbl), TrendMove.AddDouble(open));
            Assert.AreEqual(5, s.Summaries.Single().Counts[0]);
            Assert.IsFalse(s.Events.Any(e => e.Kind == EventKind.Sold));
        }

        [Test]
        public void PlayersWithoutCards_AreSkipped_AndEmptyHandsEndTheRound()
        {
            int card = C(0, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { card, C(1, AuctionType.Open) }, new int[0], new[] { C(2, AuctionType.Open) } });
            s = Apply(s, TrendMove.Play(card), TrendMove.Pass(), TrendMove.Pass(), TrendMove.Pass());
            Assert.AreEqual(2, s.CurrentPlayer, "手札の無い p1 は飛ばす");

            var empty = TrendSetup.CreateCustom(new[] { new[] { card }, new int[0], new int[0] }, deck: TrendCards.AllCards());
            empty = Apply(empty, TrendMove.Play(card), TrendMove.Pass(), TrendMove.Pass(), TrendMove.Pass());
            Assert.AreEqual(1, empty.Round, "全員の手札が無くなったらラウンド終了");
            CollectionAssert.AreEqual(new[] { 30, 0, 0, 0, 0 }, empty.Summaries[0].CardValues);
            Assert.AreEqual(130, empty.Money[0], "引き取った絵が売れる");
        }

        // ---------- 勝敗 ----------

        [Test]
        public void LastRound_EndsGame_RichestWins()
        {
            int fifth = C(0, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { fifth }, new[] { C(1, AuctionType.Open) }, new[] { C(2, AuctionType.Open) } },
                new[] { 50, 120, 90 }, 0, 3, new[] { 4, 0, 0, 0, 0 });
            s = Apply(s, TrendMove.Play(fifth));
            Assert.AreEqual(TrendPhase.GameOver, s.Phase);
            Assert.AreEqual(-1, s.CurrentPlayer);
            Assert.AreEqual(GameStatus.Win, s.Result.Status);
            Assert.AreEqual(1, s.Result.Winner);
            Assert.AreEqual(EventKind.GameEnd, s.Events.Last().Kind);
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
            var view = TrendPlayerView.Create(s, 0);
            CollectionAssert.AreEqual(new[] { 50, 120, 90 }, view.Money, "終局後は全員の所持金を公開");
        }

        [Test]
        public void LastRound_TieForFirst_IsDraw()
        {
            int fifth = C(0, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { fifth }, new[] { C(1, AuctionType.Open) }, new[] { C(2, AuctionType.Open) } },
                new[] { 120, 120, 90 }, 0, 3, new[] { 4, 0, 0, 0, 0 });
            s = Apply(s, TrendMove.Play(fifth));
            Assert.AreEqual(GameStatus.Draw, s.Result.Status);
        }

        [Test]
        public void View_HidesOthersHandsAndMoney()
        {
            var s = TrendSetup.CreateRandom(new Random(3), 4);
            var v = TrendPlayerView.Create(s, 1);
            CollectionAssert.AreEqual(s.Hands[1], v.Hand);
            CollectionAssert.AreEqual(new[] { -1, 100, -1, -1 }, v.Money);
            CollectionAssert.AreEqual(new[] { 9, 9, 9, 9 }, v.HandCounts);
        }
    }
}
