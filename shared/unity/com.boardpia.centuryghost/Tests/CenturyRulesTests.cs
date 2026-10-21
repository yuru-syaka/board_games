using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.CenturyGhost.Tests
{
    public class CenturyRulesTests
    {
        readonly CenturyRules _rules = new CenturyRules();

        static Spirits S(string s) => Spirits.Parse(s);

        static PlayerData Player(string spirits, int[] hand = null, int[] played = null, int[] ghosts = null, int gold = 0, int silver = 0) =>
            new PlayerData(S(spirits), hand ?? new int[0], played ?? new int[0], ghosts ?? new int[0], gold, silver);

        static Table DefaultTable(int gold = CenturyLogic.InitialGold, int silver = CenturyLogic.InitialSilver) =>
            new Table(new[] { 10, 11, 12, 13, 14, 15 }, Enumerable.Repeat(Spirits.Empty, 6), new[] { 0, 1, 2, 3, 4 }, gold, silver);

        static CenturyState State(PlayerData p0, PlayerData p1 = null, Table table = null, int current = 0, int first = 0,
            bool finalRound = false, int ply = 0) =>
            CenturySetup.CreateCustom(p0, p1 ?? Player("YYY", new[] { 2, 3 }), table ?? DefaultTable(), new[] { 16, 17, 18 },
                new[] { 5, 6, 7 }, first, current, ply, finalRound);

        // ---------- 準備 ----------

        [Test]
        public void Setup_DealsRowsAndStartingSpirits()
        {
            var s = CenturySetup.Create(CardCatalog.MarketCardIds().ToList(), Enumerable.Range(0, CardCatalog.GhostCount).ToList(), 1);
            Assert.AreEqual(CenturyLogic.MarketSize, s.Table.Market.Count);
            Assert.AreEqual(CenturyLogic.GhostRowSize, s.Table.GhostRow.Count);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(S("YYY"), s.Players[1].Spirits, "先手は黄 3 個");
            Assert.AreEqual(S("YYYY"), s.Players[0].Spirits, "後手は黄 4 個");
            CollectionAssert.AreEqual(new[] { 0, 1 }, s.Players[0].Hand);
            CollectionAssert.AreEqual(new[] { 2, 3 }, s.Players[1].Hand);
            Assert.AreEqual(CardCatalog.MarketCardIds().Count() - 6, s.MarketDeck.Count);
            Assert.AreEqual(CardCatalog.GhostCount - 5, s.GhostDeck.Count);
        }

        [Test]
        public void CreateRandom_IsShuffledAndValid()
        {
            var s = CenturySetup.CreateRandom(new Random(5));
            CollectionAssert.AreEquivalent(CardCatalog.MarketCardIds(), s.Table.Market.Concat(s.MarketDeck));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, CardCatalog.GhostCount), s.Table.GhostRow.Concat(s.GhostDeck));
            Assert.IsFalse(s.Result.IsOver);
        }

        // ---------- 術を使う ----------

        [Test]
        public void PlayProduce_AddsSpiritsAndMovesCardToPlayed()
        {
            var s = _rules.ApplyMove(State(Player("Y", new[] { 0, 1 })), CenturyMove.Play(0));
            var p = s.Players[0];
            Assert.AreEqual(S("YYY"), p.Spirits);
            CollectionAssert.AreEqual(new[] { 1 }, p.Hand);
            CollectionAssert.AreEqual(new[] { 0 }, p.Played);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(1, s.Ply);
        }

        [Test]
        public void PlayTrade_RepeatsGivenTimes()
        {
            var s = _rules.ApplyMove(State(Player("YYYYYC", new[] { 18 })), CenturyMove.Trade(18, 2)); // YY → C
            Assert.AreEqual(S("YCCC"), s.Players[0].Spirits);
        }

        [Test]
        public void PlayTrade_TooManyTimes_IsIllegal()
        {
            var s = State(Player("YYY", new[] { 18 }));
            Assert.IsTrue(_rules.IsLegal(s, CenturyMove.Trade(18, 1)));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Trade(18, 2)));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Trade(18, 0)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, CenturyMove.Trade(18, 2)));
        }

        [Test]
        public void PlayUpgrade_AppliesStepsInOrder()
        {
            var s = _rules.ApplyMove(State(Player("YY", new[] { 1 })), CenturyMove.Upgrade(1, 0, 1)); // Y→C, C→M
            Assert.AreEqual(S("YM"), s.Players[0].Spirits);
        }

        [Test]
        public void PlayUpgrade_InvalidSequences_AreIllegal()
        {
            var s = State(Player("YV", new[] { 1 }));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Upgrade(1)), "0 回は不可");
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Upgrade(1, 3)), "紫は上げられない");
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Upgrade(1, 1)), "持っていない青は上げられない");
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Upgrade(1, 0, 1, 2)), "強化 2 で 3 回は不可");
            Assert.IsTrue(_rules.IsLegal(s, CenturyMove.Upgrade(1, 0, 1)));
        }

        [Test]
        public void PlayCardNotInHand_IsIllegal()
        {
            var s = State(Player("YY", new[] { 1 }, new[] { 0 }));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Play(0)), "使用済み");
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Play(10)), "市場のカード");
        }

        [Test]
        public void UpgradeSequences_AreDistinctByResult()
        {
            var seqs = CenturyLogic.UpgradeSequences(S("YY"), 2);
            var results = seqs.Select(q => { CenturyLogic.TryUpgrade(S("YY"), q, out var r); return r; }).ToList();
            Assert.AreEqual(results.Count, results.Distinct().Count());
            // YY → YC / CC / YM
            CollectionAssert.AreEquivalent(new[] { S("YC"), S("CC"), S("YM") }, results);
        }

        // ---------- 術を覚える ----------

        [Test]
        public void AcquireLeftmost_IsFree()
        {
            var s = _rules.ApplyMove(State(Player("", new[] { 0 })), CenturyMove.Acquire(0));
            var p = s.Players[0];
            CollectionAssert.AreEqual(new[] { 0, 10 }, p.Hand);
            CollectionAssert.AreEqual(new[] { 11, 12, 13, 14, 15, 16 }, s.Table.Market, "左に詰めて山札から補充");
            Assert.AreEqual(2, s.MarketDeck.Count);
        }

        [Test]
        public void Acquire_PaysLowestSpiritsOntoCardsToTheLeft()
        {
            var s = _rules.ApplyMove(State(Player("YCM")), CenturyMove.Acquire(2));
            Assert.AreEqual(S("M"), s.Players[0].Spirits);
            CollectionAssert.AreEqual(new[] { 10, 11, 13, 14, 15, 16 }, s.Table.Market);
            Assert.AreEqual(S("Y"), s.Table.MarketSpirits[0]);
            Assert.AreEqual(S("C"), s.Table.MarketSpirits[1]);
            Assert.AreEqual(Spirits.Empty, s.Table.MarketSpirits[2]);
        }

        [Test]
        public void Acquire_TakesSpiritsPlacedOnTheCard()
        {
            var table = new Table(new[] { 10, 11, 12 }, new[] { S("YY"), S("C"), Spirits.Empty }, new[] { 0 }, 4, 4);
            var s = _rules.ApplyMove(State(Player("V"), table: table), CenturyMove.Acquire(1));
            Assert.AreEqual(S("C"), s.Players[0].Spirits, "V を左に置き、C をもらう");
            Assert.AreEqual(S("YYV"), s.Table.MarketSpirits[0]);
        }

        [Test]
        public void Acquire_WithoutEnoughSpirits_IsIllegal()
        {
            var s = State(Player("Y"));
            Assert.IsTrue(_rules.IsLegal(s, CenturyMove.Acquire(1)));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Acquire(2)));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Acquire(6)), "範囲外");
        }

        // ---------- おばけを呼ぶ ----------

        [Test]
        public void Claim_PaysCostAndTakesGold()
        {
            var s = _rules.ApplyMove(State(Player("YYCCV")), CenturyMove.Claim(0)); // ghost 0 = YYCC
            var p = s.Players[0];
            Assert.AreEqual(S("V"), p.Spirits);
            CollectionAssert.AreEqual(new[] { 0 }, p.Ghosts);
            Assert.AreEqual(1, p.Gold);
            Assert.AreEqual(3, s.Table.GoldLeft);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, s.Table.GhostRow);
            Assert.AreEqual(6 + 3 + 1, p.Score);
        }

        [Test]
        public void Claim_SecondPosition_TakesSilver()
        {
            var s = _rules.ApplyMove(State(Player("YYYCC")), CenturyMove.Claim(1)); // ghost 1 = YYYCC
            Assert.AreEqual(1, s.Players[0].Silver);
            Assert.AreEqual(0, s.Players[0].Gold);
        }

        [Test]
        public void Claim_WhenGoldIsGone_SilverMovesToFirstPosition()
        {
            var s = _rules.ApplyMove(State(Player("YYCC"), table: DefaultTable(gold: 0, silver: 2)), CenturyMove.Claim(0));
            Assert.AreEqual(1, s.Players[0].Silver);
            var s2 = _rules.ApplyMove(State(Player("YYYCC"), table: DefaultTable(gold: 0, silver: 2)), CenturyMove.Claim(1));
            Assert.AreEqual(0, s2.Players[0].Silver, "金貨が無いと 2 番目にはコインなし");
        }

        [Test]
        public void Claim_RequiresExactSpirits()
        {
            var s = State(Player("YYCM"));
            Assert.IsFalse(_rules.IsLegal(s, CenturyMove.Claim(0)), "YYCC が必要");
            Assert.IsTrue(_rules.IsLegal(State(Player("YYCCC")), CenturyMove.Claim(0)));
        }

        // ---------- 休む・上限 ----------

        [Test]
        public void Rest_ReturnsPlayedCards()
        {
            var s = _rules.ApplyMove(State(Player("Y", new[] { 1 }, new[] { 0, 10 })), CenturyMove.Rest());
            CollectionAssert.AreEquivalent(new[] { 0, 1, 10 }, s.Players[0].Hand);
            Assert.AreEqual(0, s.Players[0].Played.Count);
        }

        [Test]
        public void Rest_WithNothingPlayed_IsIllegal()
        {
            Assert.IsFalse(_rules.IsLegal(State(Player("Y", new[] { 0 })), CenturyMove.Rest()));
        }

        [Test]
        public void Overflow_DiscardsLowestDownToTen()
        {
            var s = _rules.ApplyMove(State(Player("YYYYCCCMV", new[] { 15 })), CenturyMove.Play(15)); // +YYYY → 13 個
            Assert.AreEqual(S("YCCCMV").Plus(S("YYYY")), s.Players[0].Spirits);
            Assert.AreEqual(CenturyLogic.MaxSpirits, s.Players[0].Spirits.Total);
        }

        // ---------- 終了 ----------

        [Test]
        public void FirstPlayerReachesGoal_SecondPlayerGetsOneMoreTurn()
        {
            var p0 = Player("YYCC", ghosts: new[] { 10, 11, 12 });
            var p1 = Player("", new[] { 2 });
            var s = _rules.ApplyMove(State(p0, p1, first: 0, current: 0), CenturyMove.Claim(0));
            Assert.IsFalse(s.Result.IsOver);
            Assert.IsTrue(s.FinalRound);
            s = _rules.ApplyMove(s, CenturyMove.Play(2));
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(EndReason.GhostsCollected, s.EndReason);
            Assert.AreEqual(GameResult.WinFor(0), s.Result);
        }

        [Test]
        public void SecondPlayerReachesGoal_EndsImmediately()
        {
            var p0 = Player("Y", new[] { 0 });
            var p1 = Player("YYCC", ghosts: new[] { 10, 11, 12 });
            var s = _rules.ApplyMove(State(p0, p1, first: 0, current: 1), CenturyMove.Claim(0));
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(GameResult.WinFor(1), s.Result);
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void EqualScore_IsDraw()
        {
            var p0 = Player("YYCC", new[] { 0 }, ghosts: new[] { 5, 6, 7 }); // 10+10+11 = 31
            var p1 = Player("", new[] { 2 }, ghosts: new[] { 0, 1, 8, 13 }, gold: 1, silver: 1); // 6+7+11+13+3+1 = 41
            var s = State(p0, p1, first: 0, current: 0, table: DefaultTable());
            s = _rules.ApplyMove(s, CenturyMove.Claim(0)); // +6 +gold3 = 40, 4 体目
            Assert.IsTrue(s.FinalRound);
            Assert.IsFalse(s.Result.IsOver);
            s = _rules.ApplyMove(s, CenturyMove.Play(2)); // P1 +YY(0 点)
            Assert.AreEqual(40, s.Players[0].Score);
            Assert.AreEqual(41, s.Players[1].Score);
            Assert.AreEqual(GameResult.WinFor(1), s.Result);

            var q0 = Player("YYCC", new[] { 0 }, ghosts: new[] { 5, 6, 7 });
            var q1 = Player("", new[] { 2 }, ghosts: new[] { 0, 1, 8, 13 }, gold: 1); // 37 + 3 = 40
            var d = _rules.ApplyMove(_rules.ApplyMove(State(q0, q1), CenturyMove.Claim(0)), CenturyMove.Play(2));
            Assert.AreEqual(d.Players[0].Score, d.Players[1].Score);
            Assert.AreEqual(GameStatus.Draw, d.Result.Status);
        }

        [Test]
        public void TurnLimit_EndsWithScores()
        {
            var s = State(Player("C", new[] { 0 }), Player("", new[] { 2 }), ply: CenturyLogic.MaxPlies - 1);
            s = _rules.ApplyMove(s, CenturyMove.Play(0));
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(EndReason.TurnLimit, s.EndReason);
            Assert.AreEqual(GameResult.WinFor(0), s.Result);
        }

        // ---------- 合法手全体 ----------

        [Test]
        public void RandomPlayouts_AllGeneratedMovesAreLegalAndGamesEnd()
        {
            var rng = new Random(42);
            for (int game = 0; game < 30; game++)
            {
                var s = CenturySetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.Greater(moves.Count, 0);
                    foreach (var m in moves) Assert.IsTrue(_rules.IsLegal(s, m), m.ToString());
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                    foreach (var p in s.Players) Assert.LessOrEqual(p.Spirits.Total, CenturyLogic.MaxSpirits);
                }
                Assert.LessOrEqual(s.Ply, CenturyLogic.MaxPlies);
            }
        }
    }
}
