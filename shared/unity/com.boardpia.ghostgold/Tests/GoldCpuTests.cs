using System;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.GhostGold.Tests
{
    public class GoldCpuTests
    {
        readonly GoldRules _rules = new GoldRules();

        /// <summary>CPU だけで最後まで遊ぶ。毎手、カードの数が合っていること・手番の人が探検中であることを確かめる。</summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void CpuGames_AlwaysFinishAfterFiveNights(int players)
        {
            int returnedAlone = 0, scared = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var rng = new Random(seed);
                var cpus = Enumerable.Range(0, players).Select(_ => new GoldCpu(new Random(rng.Next()))).ToArray();
                var s = GoldSetup.Create(players, seed);
                int guard = 0;
                while (!s.Result.IsOver)
                {
                    Assert.Less(guard++, 2000, $"seed {seed}: 止まらない");
                    AssertCardsAddUp(s, seed);
                    Assert.IsTrue(s.Current.Exploring, $"seed {seed}: 手番の人が探検中でない");
                    Assert.IsFalse(s.Current.Decided);
                    var move = cpus[s.CurrentPlayer].ChooseMove(GoldPlayerView.Create(s, s.CurrentPlayer));
                    Assert.IsTrue(_rules.IsLegal(s, move));
                    s = _rules.ApplyMove(s, move);
                }
                Assert.AreEqual(GoldLogic.Nights, s.Events.Count(e => e.Kind == EventKind.NightEnd), $"seed {seed}: 夜の数");
                Assert.IsTrue(s.Players.All(p => p.Carry == 0 && !p.Exploring));
                Assert.AreEqual(1, s.Events.Count(e => e.Kind == EventKind.GameEnd));
                returnedAlone += s.Events.Count(e => e.Kind == EventKind.RelicTaken);
                scared += s.Events.Count(e => e.Kind == EventKind.Scared);
            }
            // CPU は帰ることも、おどかされることもある(いつも同じ動きではない)
            Assert.Greater(returnedAlone, 0);
            Assert.Greater(scared, 0);
        }

        [Test]
        public void Cpu_GoesOnWhenThereIsNothingToLose()
        {
            var s = TestState.Make(3, new[] { TestState.Gold1 }, new[] { TestState.Gold5 });
            var cpu = new GoldCpu(new Random(1));
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(GoldMove.Go, cpu.ChooseMove(GoldPlayerView.Create(s, 0)));
        }

        [Test]
        public void Cpu_ReturnsWithALotToLoseAndHighRisk()
        {
            // 4 種類の怪異が 1 枚ずつ出ていて、山札は怪異ばかり
            var path = new[] { TestState.Polter, TestState.Wisp, TestState.Mirror, TestState.Well };
            var deck = new[] { TestState.Polter2, TestState.Wisp + 1, TestState.Mirror + 1, TestState.Well + 1, TestState.Gold1 };
            var s = TestState.Make(2, path, deck);
            s.Players[0].Carry = 30;
            s.Players[0].HasAmulet = false;
            var cpu = new GoldCpu(new Random(2));
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(GoldMove.Return, cpu.ChooseMove(GoldPlayerView.Create(s, 0)));
        }

        static void AssertCardsAddUp(GoldState s, int seed)
        {
            var all = s.Deck.Concat(s.Path).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), $"seed {seed}: 同じカードが 2 枚");
            Assert.AreEqual(GoldLogic.TreasureCount, all.Count(c => GoldCards.KindOf(c) == CardKind.Treasure), $"seed {seed}: 金貨");
            for (int k = 0; k < GoldLogic.HazardKinds; k++)
                Assert.AreEqual(GoldLogic.HazardCopies - s.HazardRemoved[k], all.Count(c => GoldCards.HazardOf(c) == k), $"seed {seed}: 怪異 {k}");
            for (int n = 1; n <= GoldLogic.RelicCount; n++)
            {
                int card = GoldCards.RelicCard(n);
                var status = s.Relics[n - 1];
                Assert.AreEqual(status == RelicStatus.InDeck, s.Deck.Contains(card), $"seed {seed}: 秘宝 {n} の山札");
                if (status == RelicStatus.OnPath) Assert.IsTrue(s.Path.Contains(card));
                if (status == RelicStatus.NotYet) Assert.Greater(n, s.Night);
            }
        }
    }
}
