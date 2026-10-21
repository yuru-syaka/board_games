using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.CenturyGhost.Tests
{
    public class CenturyCpuTests
    {
        readonly CenturyRules _rules = new CenturyRules();

        static PlayerData Player(string spirits, int[] hand, int[] played = null, int[] ghosts = null) =>
            new PlayerData(Spirits.Parse(spirits), hand, played ?? new int[0], ghosts ?? new int[0], 0, 0);

        static CenturyState State(PlayerData p0, PlayerData p1) =>
            CenturySetup.CreateCustom(p0, p1,
                new Table(new[] { 10, 11, 12, 13, 14, 15 }, Enumerable.Repeat(Spirits.Empty, 6), new[] { 0, 1, 2, 3, 4 }, 4, 4),
                new[] { 16, 17 }, new[] { 5, 6 }, 0, 0);

        [Test]
        public void ClaimsAffordableGhost()
        {
            var s = State(Player("YYCC", new[] { 0, 1 }), Player("YYY", new[] { 2, 3 }));
            var move = new CenturyCpu(new Random(1)).ChooseMove(CenturyPlayerView.Create(s, 0));
            Assert.AreEqual(CenturyMove.Claim(0), move);
        }

        [Test]
        public void RestsWhenNothingUsefulIsInHand()
        {
            // 手札は強化だけ(魂が無いので使えない)、使用済みに召集がある
            var s = State(Player("", new[] { 1 }, new[] { 0, 10 }), Player("YYY", new[] { 2, 3 }));
            var view = CenturyPlayerView.Create(s, 0);
            var move = new CenturyCpu(new Random(1)).ChooseMove(view);
            Assert.AreNotEqual(MoveKind.Play, move.Kind);
        }

        [Test]
        public void AlwaysChoosesLegalMove()
        {
            var rng = new Random(7);
            var cpu = new CenturyCpu(new Random(8));
            for (int game = 0; game < 5; game++)
            {
                var s = CenturySetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    var move = cpu.ChooseMove(CenturyPlayerView.Create(s, s.CurrentPlayer));
                    Assert.IsTrue(_rules.IsLegal(s, move), move?.ToString());
                    s = _rules.ApplyMove(s, move);
                }
            }
        }

        [Test]
        public void CpuVsCpu_GamesFinishByCollectingGhosts()
        {
            var rng = new Random(123);
            int collected = 0;
            int totalPlies = 0;
            const int games = 20;
            for (int game = 0; game < games; game++)
            {
                var cpus = new[] { new CenturyCpu(new Random(rng.Next())), new CenturyCpu(new Random(rng.Next())) };
                var s = CenturySetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                    s = _rules.ApplyMove(s, cpus[s.CurrentPlayer].ChooseMove(CenturyPlayerView.Create(s, s.CurrentPlayer)));
                if (s.EndReason == EndReason.GhostsCollected) collected++;
                totalPlies += s.Ply;
            }
            TestContext.WriteLine($"collected {collected}/{games}, avg plies {totalPlies / (double)games:F1}");
            Assert.GreaterOrEqual(collected, games - 1);
        }

        [Test]
        public void CpuBeatsRandomPlayer()
        {
            var rng = new Random(99);
            int wins = 0;
            const int games = 20;
            for (int game = 0; game < games; game++)
            {
                var cpu = new CenturyCpu(new Random(rng.Next()));
                int cpuSide = game % 2;
                var s = CenturySetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    CenturyMove move;
                    if (s.CurrentPlayer == cpuSide)
                    {
                        move = cpu.ChooseMove(CenturyPlayerView.Create(s, cpuSide));
                    }
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == cpuSide) wins++;
            }
            Assert.GreaterOrEqual(wins, games - 2);
        }
    }
}
