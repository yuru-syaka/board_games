using System;
using System.Diagnostics;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Vanish.Tests
{
    public class VanishCpuTests
    {
        readonly VanishRules _rules = new VanishRules();

        [Test]
        public void TakesImmediateWin()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var s = VanishState.FromOrder(new[] { 0, 8 }, new[] { 3, 4 }, 1);
                var cpu = new VanishCpu(new Random(seed));
                var next = _rules.ApplyMove(s, cpu.ChooseMove(s));
                Assert.AreEqual(GameResult.WinFor(1), next.Result, $"seed {seed}");
            }
        }

        [Test]
        public void BlocksOpponentsImmediateWin()
        {
            // P0 が 0,1 に並べている。P1 は 2 をふさがないと次に負ける
            for (int seed = 0; seed < 10; seed++)
            {
                var s = VanishState.FromOrder(new[] { 0, 1 }, new[] { 4 }, 1);
                var cpu = new VanishCpu(new Random(seed));
                var move = cpu.ChooseMove(s);
                Assert.AreEqual(2, move.Cell, $"seed {seed}");
            }
        }

        [Test]
        public void DoesNotRelyOnPieceThatWillVanish()
        {
            // P1 は 0(最古)・1・6。2 に置いても 0 が消えるので勝てない。
            // P0 は 3,4 に並べていて、5 をふさがないと負ける
            for (int seed = 0; seed < 10; seed++)
            {
                var s = VanishState.FromOrder(new[] { 3, 4 }, new[] { 0, 1, 6 }, 1);
                var cpu = new VanishCpu(new Random(seed));
                Assert.AreEqual(5, cpu.ChooseMove(s).Cell, $"seed {seed}");
            }
        }

        [Test]
        public void ChoosesOnlyLegalMoves()
        {
            var rng = new Random(5);
            var cpu = new VanishCpu(new Random(9));
            for (int game = 0; game < 5; game++)
            {
                var s = VanishState.Initial(game % 2);
                while (!s.Result.IsOver)
                {
                    var move = s.CurrentPlayer == 1 ? cpu.ChooseMove(s) : _rules.GetLegalMoves(s)[rng.Next(_rules.GetLegalMoves(s).Count)];
                    Assert.IsTrue(_rules.IsLegal(s, move));
                    s = _rules.ApplyMove(s, move);
                }
            }
        }

        [Test]
        public void BeatsRandomPlayerMostOfTheTime()
        {
            var rng = new Random(11);
            int cpuWins = 0, cpuLosses = 0;
            const int games = 20;
            for (int game = 0; game < games; game++)
            {
                var cpu = new VanishCpu(new Random(game));
                var s = VanishState.Initial(game % 2);
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    var move = s.CurrentPlayer == 1 ? cpu.ChooseMove(s) : moves[rng.Next(moves.Count)];
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 1) cpuWins++;
                else if (s.Result.Status == GameStatus.Win) cpuLosses++;
            }
            Assert.AreEqual(0, cpuLosses);
            Assert.GreaterOrEqual(cpuWins, games * 3 / 4);
        }

        [Test]
        public void ThinksFastEnoughOnOpeningPosition()
        {
            // WebGL は単一スレッドなので、初手(最も分岐が多い)でも十分速いこと
            var cpu = new VanishCpu(new Random(1));
            var sw = Stopwatch.StartNew();
            cpu.ChooseMove(VanishState.Initial(1));
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 500);
        }

        [Test]
        public void Throws_WhenGameIsOver()
        {
            var s = _rules.ApplyMove(VanishState.FromOrder(new[] { 0, 1 }, new[] { 4 }, 0), VanishMove.Place(2));
            Assert.Throws<InvalidOperationException>(() => new VanishCpu(new Random(0)).ChooseMove(s));
        }

        [Test]
        public void Match_PlaysCpuTurnOnlyOnCpuTurn()
        {
            var match = new VanishMatch(new Random(0), 1);
            match.StartNewGame(0);
            Assert.IsTrue(match.IsHumanTurn);
            match.PlayCpuTurn();
            Assert.AreEqual(0, match.State.Ply, "CPU must not move on the human's turn");
            Assert.IsTrue(match.SubmitMove(VanishMove.Place(4)));
            Assert.IsTrue(match.IsCpuTurn);
            Assert.IsFalse(match.SubmitMove(VanishMove.Place(0)), "human cannot move on the CPU's turn");
            match.PlayCpuTurn();
            Assert.AreEqual(2, match.State.Ply);
            Assert.IsFalse(match.SubmitMove(VanishMove.Place(4)), "occupied cell");
        }
    }
}
