using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostTrend.Tests
{
    public class TrendCpuTests
    {
        readonly TrendRules _rules = new TrendRules();

        static int C(int artist, AuctionType type, int nth = 0) => TrendCards.Find(artist, type, nth);

        [TestCase(3, 1)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(3, 4)]
        [TestCase(4, 5)]
        [TestCase(5, 6)]
        public void CpuOnlyGame_AlwaysLegal_AndFinishes(int players, int seed)
        {
            var rng = new Random(seed);
            var cpus = Enumerable.Range(0, players).Select(i => new TrendCpu(new Random(seed * 10 + i))).ToArray();
            var s = TrendSetup.CreateRandom(rng, players);
            for (int i = 0; i < 5000 && !s.Result.IsOver; i++)
            {
                var view = TrendPlayerView.Create(s, s.CurrentPlayer);
                var move = cpus[s.CurrentPlayer].ChooseMove(view);
                Assert.IsTrue(_rules.IsLegal(s, move), $"illegal {move} in {s.Phase}");
                s = _rules.ApplyMove(s, move);
                Assert.IsTrue(s.Money.All(m => m >= 0), "所持金はマイナスにならない");
            }
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(TrendLogic.Rounds, s.Summaries.Count);
            Assert.Greater(s.Summaries.Sum(r => r.Payouts.Sum()), 0, "絵が売れている");
        }

        [Test]
        public void RandomLegalGame_Finishes()
        {
            var rng = new Random(11);
            var s = TrendSetup.CreateRandom(rng, 4);
            for (int i = 0; i < 100000 && !s.Result.IsOver; i++)
            {
                var moves = _rules.GetLegalMoves(s);
                Assert.Greater(moves.Count, 0);
                // 値上げばかりにならないよう、半分は降りる系の手を選ぶ
                var quit = moves.Where(m => m.Kind == MoveKind.Pass || m.Kind == MoveKind.Decline).ToList();
                var move = quit.Count > 0 && rng.Next(2) == 0 ? quit[0] : moves[rng.Next(moves.Count)];
                s = _rules.ApplyMove(s, move);
            }
            Assert.IsTrue(s.Result.IsOver);
        }

        [Test]
        public void Cpu_AddsPartnerToOwnDouble_DeclinesWithoutOne()
        {
            int dbl = C(4, AuctionType.Double), open = C(4, AuctionType.Open);
            var s = TrendSetup.CreateCustom(new[] { new[] { dbl, open, C(0, AuctionType.Open) }, new[] { C(1, AuctionType.Open) },
                new[] { C(2, AuctionType.Open) } }, null, 0, 0, new[] { 0, 0, 0, 0, 2 });
            s = _rules.ApplyMove(s, TrendMove.Play(dbl));
            var move = new TrendCpu(new Random(1)).ChooseMove(TrendPlayerView.Create(s, 0));
            Assert.AreEqual(TrendMove.AddDouble(open), move);

            var s2 = TrendSetup.CreateCustom(new[] { new[] { dbl, C(0, AuctionType.Open) }, new[] { C(1, AuctionType.Open) },
                new[] { C(2, AuctionType.Open) } });
            s2 = _rules.ApplyMove(s2, TrendMove.Play(dbl));
            Assert.AreEqual(TrendMove.Decline(), new TrendCpu(new Random(1)).ChooseMove(TrendPlayerView.Create(s2, 0)));
        }

        [Test]
        public void Cpu_DoesNotBidMoreThanItHas()
        {
            int card = C(4, AuctionType.Sealed);
            var s = TrendSetup.CreateCustom(new[] { new[] { card }, new[] { C(1, AuctionType.Open) }, new[] { C(2, AuctionType.Open) } },
                new[] { 100, 3, 100 }, 0, 0, new[] { 0, 0, 0, 0, 3 },
                new[] { new[] { 0, 0, 0, 0, 30 }, new int[5], new int[5], new int[5] });
            s = _rules.ApplyMove(s, TrendMove.Play(card));
            var move = new TrendCpu(new Random(2)).ChooseMove(TrendPlayerView.Create(s, 1));
            Assert.AreEqual(MoveKind.Bid, move.Kind);
            Assert.LessOrEqual(move.Amount, 3);
        }

        [Test]
        public void Cpu_ValuesLeadingArtistHigher()
        {
            var s = TrendSetup.CreateCustom(new[] { new[] { C(4, AuctionType.Open) }, new[] { C(1, AuctionType.Open) },
                new[] { C(2, AuctionType.Open) } }, null, 0, 0, new[] { 0, 0, 0, 0, 4 });
            var est = TrendCpu.EstimateValues(TrendPlayerView.Create(s, 1));
            Assert.Greater(est[4], est[0]);
            Assert.Greater(est[4], 20);
        }
    }
}
