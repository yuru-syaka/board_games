using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostJewel.Tests
{
    public class JewelRulesTests
    {
        readonly JewelRules _rules = new JewelRules();

        static JewelState NewGame(int players, int seed = 1)
        {
            var s = JewelSetup.CreateRandom(new Random(seed), players);
            // テストでは P0 から始める
            s.StartPlayer = 0;
            s.CurrentPlayer = 0;
            return s;
        }

        [Test]
        public void CardsAndNobles_HaveTheExpectedCounts()
        {
            Assert.AreEqual(90, JewelCards.All.Count);
            Assert.AreEqual(40, JewelCards.TierIds(0).Count);
            Assert.AreEqual(30, JewelCards.TierIds(1).Count);
            Assert.AreEqual(20, JewelCards.TierIds(2).Count);
            Assert.AreEqual(10, JewelCards.Nobles.Count);
            for (int color = 0; color < Gem.Colors; color++)
                Assert.AreEqual(18, JewelCards.All.Count(c => c.Bonus == color), "色ごとに同じ枚数");
        }

        [TestCase(2, 4)]
        [TestCase(3, 5)]
        [TestCase(4, 7)]
        public void Setup_DealsGemsMarketAndNobles(int players, int each)
        {
            var s = NewGame(players);
            for (int c = 0; c < Gem.Colors; c++) Assert.AreEqual(each, s.Bank[c]);
            Assert.AreEqual(5, s.Bank[Gem.Gold]);
            Assert.AreEqual(players + 1, s.Nobles.Count);
            for (int t = 0; t < JewelLogic.Tiers; t++)
                Assert.IsTrue(s.Market[t].All(c => c >= 0 && JewelCards.Get(c).Tier == t));
            Assert.AreEqual(40 - 4, s.Decks[0].Count);
        }

        [Test]
        public void TakeThreeDifferent_MovesGemsFromBank()
        {
            var s = NewGame(2);
            var next = _rules.ApplyMove(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.AreEqual(1, next.Players[0].Gems[Gem.Aqua]);
            Assert.AreEqual(3, next.Bank[Gem.Aqua]);
            Assert.AreEqual(1, next.CurrentPlayer);
            Assert.AreEqual(0, s.Players[0].Gems[Gem.Aqua], "元の局面は変わらない");
        }

        [Test]
        public void TakeTwo_NeedsFourInThePile()
        {
            var s = NewGame(2);
            Assert.IsTrue(_rules.IsLegal(s, JewelMove.TakeTwo(Gem.Aqua)));
            s.Bank[Gem.Aqua] = 3;
            Assert.IsFalse(_rules.IsLegal(s, JewelMove.TakeTwo(Gem.Aqua)));
        }

        [Test]
        public void TakeGems_TakesFewerWhenFewColorsRemain()
        {
            var s = NewGame(2);
            for (int c = 2; c < Gem.Colors; c++) s.Bank[c] = 0;
            Assert.IsTrue(_rules.IsLegal(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose)));
            Assert.IsFalse(_rules.IsLegal(s, JewelMove.TakeGemsOf(Gem.Aqua)));
        }

        [Test]
        public void Reserve_GivesGoldAndRefillsTheMarket()
        {
            var s = NewGame(2);
            int card = s.Market[0][1];
            int top = s.Decks[0].Last();
            var next = _rules.ApplyMove(s, JewelMove.Reserve(card));
            Assert.AreEqual(1, next.Players[0].Gems[Gem.Gold]);
            Assert.AreEqual(new[] { card }, next.Players[0].Reserved.ToArray());
            Assert.AreEqual(top, next.Market[0][1]);
        }

        [Test]
        public void Reserve_IsLimitedToThree()
        {
            var s = NewGame(2);
            s.Players[0].Reserved.AddRange(new[] { 0, 1, 2 });
            s.Players[0].ReservedFromDeck.AddRange(new[] { false, false, false });
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Kind == MoveKind.Reserve || m.Kind == MoveKind.ReserveDeck));
        }

        [Test]
        public void Buy_UsesBonusThenGemsThenGold()
        {
            var s = NewGame(2);
            int card = s.Market[0][0];
            var cost = JewelCards.Get(card).Cost;
            var me = s.Players[0];
            // 1 色だけゴールドで払うように持たせる
            int goldColor = Enumerable.Range(0, Gem.Colors).First(c => cost[c] > 0);
            for (int c = 0; c < Gem.Colors; c++)
            {
                int need = cost[c];
                if (c == goldColor)
                {
                    me.Bonus[c] = need - 1;
                    me.Gems[Gem.Gold] = 1;
                }
                else me.Gems[c] = need;
                s.Bank[c] -= c == goldColor ? 0 : need;
            }
            s.Bank[Gem.Gold] -= 1;
            Assert.IsTrue(_rules.IsLegal(s, JewelMove.Buy(card)));
            var next = _rules.ApplyMove(s, JewelMove.Buy(card));
            Assert.AreEqual(0, next.Players[0].GemTotal);
            Assert.AreEqual(JewelCards.Get(card).Points, next.Players[0].Points);
            Assert.AreEqual(1, next.Players[0].Bought.Count);
            Assert.AreEqual(5, next.Bank[Gem.Gold]);
        }

        [Test]
        public void TooManyGems_MustBeReturnedBeforeTheTurnEnds()
        {
            var s = NewGame(2);
            s.Players[0].Gems[Gem.Emerald] = 9;
            s.Bank[Gem.Emerald] = 0;
            var next = _rules.ApplyMove(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.AreEqual(JewelPhase.Return, next.Phase);
            Assert.AreEqual(0, next.CurrentPlayer);
            Assert.IsTrue(_rules.GetLegalMoves(next).All(m => m.Kind == MoveKind.Return));
            next = _rules.ApplyMove(next, JewelMove.Return(Gem.Emerald));
            Assert.AreEqual(JewelPhase.Return, next.Phase, "まだ 11 個");
            next = _rules.ApplyMove(next, JewelMove.Return(Gem.Emerald));
            Assert.AreEqual(JewelPhase.Main, next.Phase, "10 個になったら手番が終わる");
            Assert.AreEqual(1, next.CurrentPlayer);
            Assert.AreEqual(10, next.Players[0].GemTotal);
        }

        [Test]
        public void Noble_VisitsAtTheEndOfTheTurn()
        {
            var s = NewGame(2);
            int noble = s.Nobles[0];
            var need = JewelCards.GetNoble(noble).Need;
            for (int c = 0; c < Gem.Colors; c++) s.Players[0].Bonus[c] = need[c];
            var next = _rules.ApplyMove(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.AreEqual(new[] { noble }, next.Players[0].Nobles.ToArray());
            Assert.AreEqual(3, next.Players[0].Points);
            Assert.IsFalse(next.Nobles.Contains(noble));
        }

        [Test]
        public void ReachingFifteen_FinishesTheRoundThenEnds()
        {
            var s = NewGame(3);
            s.Players[0].Points = 15;
            var next = _rules.ApplyMove(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.IsTrue(next.FinalRound);
            Assert.IsFalse(next.Result.IsOver, "残りの人も最後の手番をする");
            next = _rules.ApplyMove(next, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.IsFalse(next.Result.IsOver);
            next = _rules.ApplyMove(next, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.IsTrue(next.Result.IsOver);
            Assert.AreEqual(GameResult.WinFor(0).ToString(), next.Result.ToString());
        }

        [Test]
        public void Tie_GoesToTheFewerCards()
        {
            var s = NewGame(2);
            s.Players[0].Points = 15;
            s.Players[1].Points = 15;
            s.Players[0].Bought.AddRange(new[] { 1, 2, 3 });
            s.Players[1].Bought.AddRange(new[] { 4, 5 });
            var next = _rules.ApplyMove(s, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            next = _rules.ApplyMove(next, JewelMove.TakeGemsOf(Gem.Aqua, Gem.Rose, Gem.Topaz));
            Assert.AreEqual(GameStatus.Win, next.Result.Status);
            Assert.AreEqual(1, next.Result.Winner);
        }

        [Test]
        public void PlayerView_HidesOthersDeckReservations()
        {
            var s = NewGame(2);
            s = _rules.ApplyMove(s, JewelMove.ReserveDeck(2));
            int hidden = s.Players[0].Reserved[0];
            var opponentView = JewelPlayerView.Create(s, 1);
            Assert.AreEqual(-1, opponentView.PlayerAt(0).Reserved[0]);
            Assert.IsTrue(opponentView.Events.Where(e => e.Kind == EventKind.Reserved).All(e => e.Card == -1));
            var myView = JewelPlayerView.Create(s, 0);
            Assert.AreEqual(hidden, myView.PlayerAt(0).Reserved[0]);
        }
    }
}
