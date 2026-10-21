using System;
using System.Diagnostics;
using BoardGames.Core;
using NUnit.Framework;
using static BoardGames.BattleLine.Tests.TestBoard;

namespace BoardGames.BattleLine.Tests
{
    public class BattleLineCpuTests
    {
        readonly BattleLineRules _rules = new BattleLineRules();

        /// <summary>CPU 同士(またはランダム)で最後まで指し、毎手の合法性とカードの保存を確かめる。</summary>
        [Test]
        public void CpuVsRandom_AlwaysLegalAndGamesFinish()
        {
            var rng = new Random(7);
            var cpu = new BattleLineCpu(new Random(11));
            for (int game = 0; game < 8; game++)
            {
                var s = BattleLineSetup.CreateRandom(rng);
                int guard = 0;
                while (!s.Result.IsOver)
                {
                    BattleLineMove move;
                    if (s.CurrentPlayer == 1)
                    {
                        move = cpu.ChooseMove(BattleLinePlayerView.Create(s, 1));
                        Assert.IsTrue(_rules.IsLegal(s, move), $"illegal CPU move {move}");
                    }
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                    Assert.AreEqual(70, CountAll(s), "カードの枚数が保存されている");
                    Assert.Less(++guard, 2000, "終わらない");
                }
            }
        }

        [Test]
        public void CpuVsCpu_GamesFinish()
        {
            var rng = new Random(21);
            var a = new BattleLineCpu(new Random(1));
            var b = new BattleLineCpu(new Random(2));
            for (int game = 0; game < 4; game++)
            {
                var s = BattleLineSetup.CreateRandom(rng);
                int guard = 0;
                while (!s.Result.IsOver)
                {
                    var cpu = s.CurrentPlayer == 0 ? a : b;
                    s = _rules.ApplyMove(s, cpu.ChooseMove(BattleLinePlayerView.Create(s, s.CurrentPlayer)));
                    Assert.Less(++guard, 2000);
                }
            }
        }

        [Test]
        public void Cpu_BeatsRandomMostOfTheTime()
        {
            var rng = new Random(33);
            var cpu = new BattleLineCpu(new Random(5));
            int wins = 0;
            const int games = 10;
            for (int game = 0; game < games; game++)
            {
                var s = BattleLineSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    BattleLineMove move;
                    if (s.CurrentPlayer == 1) move = cpu.ChooseMove(BattleLinePlayerView.Create(s, 1));
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 1) wins++;
            }
            Assert.GreaterOrEqual(wins, 8, $"CPU wins {wins}/{games}");
        }

        [Test]
        public void Cpu_TakesWinningMove()
        {
            var s = Empty(1);
            Claim(s, 1, 3, 5);
            Put(s, 1, 4, T(R, 8), T(R, 9));
            Hand(s, 1, T(R, 10), T(B, 1), T(G, 2), T(Y, 3));
            var move = new BattleLineCpu(new Random(1)).ChooseMove(BattleLinePlayerView.Create(s, 1));
            Assert.AreEqual(BattleLineMove.Place(T(R, 10), 4), move);
        }

        [Test]
        public void Cpu_ExtendsFormation()
        {
            var s = Empty(1);
            Put(s, 1, 2, T(G, 6), T(G, 7));
            Put(s, 0, 2, T(B, 2));
            Hand(s, 1, T(G, 8), T(R, 1), T(Y, 1), T(P, 2));
            var move = new BattleLineCpu(new Random(1)).ChooseMove(BattleLinePlayerView.Create(s, 1));
            Assert.AreEqual(BattleLineMove.Place(T(G, 8), 2), move);
        }

        [Test]
        public void Cpu_ThinksFastEnough()
        {
            var rng = new Random(4);
            var cpu = new BattleLineCpu(new Random(4));
            var s = BattleLineSetup.CreateRandom(rng);
            // 中盤まで進める
            for (int i = 0; i < 40 && !s.Result.IsOver; i++)
            {
                var moves = _rules.GetLegalMoves(s);
                s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
            }
            Hand(s, s.CurrentPlayer, Card.Redeploy, Card.Traitor, Card.Deserter, T(R, 10), T(B, 10), T(G, 10), T(Y, 10));
            s.Phase = TurnPhase.Play;
            var sw = Stopwatch.StartNew();
            cpu.ChooseMove(BattleLinePlayerView.Create(s, s.CurrentPlayer));
            Assert.Less(sw.ElapsedMilliseconds, 1500, "WebGL でも固まらない程度");
        }
    }
}
