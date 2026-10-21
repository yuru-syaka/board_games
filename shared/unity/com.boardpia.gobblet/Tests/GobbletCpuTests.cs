using System;
using System.Collections.Generic;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Gobblet.Tests
{
    public class GobbletCpuTests
    {
        const PieceSize S = PieceSize.Small;
        const PieceSize M = PieceSize.Medium;
        const PieceSize L = PieceSize.Large;

        readonly GobbletRules _rules = new GobbletRules();

        static Piece A(PieceSize size) => new Piece(0, size);
        static Piece B(PieceSize size) => new Piece(1, size);

        static GobbletState Board(int current, params (int cell, Piece[] stack)[] cells)
        {
            var stacks = new IReadOnlyList<Piece>[GobbletState.CellCount];
            foreach (var (cell, stack) in cells) stacks[cell] = stack;
            return GobbletState.FromBoard(stacks, current);
        }

        static (int, Piece[]) At(int cell, params Piece[] stack) => (cell, stack);

        [Test]
        public void TakesImmediateWin()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var s = Board(1, At(3, B(S)), At(4, B(M)), At(0, A(S)), At(8, A(M)));
                var cpu = new GobbletCpu(new Random(seed));
                var next = _rules.ApplyMove(s, cpu.ChooseMove(s));
                Assert.AreEqual(GameResult.WinFor(1), next.Result, $"seed {seed}");
            }
        }

        [Test]
        public void BlocksOpponentsImmediateWin()
        {
            // A が 0,1 に小を並べている。B は 2 を大でふさがないと次に負ける
            for (int seed = 0; seed < 10; seed++)
            {
                var s = Board(1, At(0, A(S)), At(1, A(S)), At(4, B(S)));
                var cpu = new GobbletCpu(new Random(seed));
                var move = cpu.ChooseMove(s);
                var next = _rules.ApplyMove(s, move);
                Assert.IsFalse(next.Result.IsOver);
                // どう応じても A に即勝ちされないこと
                foreach (var reply in _rules.GetLegalMoves(next))
                    Assert.AreNotEqual(GameResult.WinFor(0), _rules.ApplyMove(next, reply).Result,
                        $"seed {seed}: {move} then {reply}");
            }
        }

        [Test]
        public void DoesNotLiftPieceThatRevealsOpponentLine()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var s = Board(1, At(0, A(S)), At(1, A(M)), At(2, A(S), B(M)));
                var cpu = new GobbletCpu(new Random(seed));
                var next = _rules.ApplyMove(s, cpu.ChooseMove(s));
                Assert.AreNotEqual(GameResult.WinFor(0), next.Result, $"seed {seed}");
            }
        }

        [Test]
        public void AlwaysReturnsLegalMove_InRandomPositions()
        {
            var rng = new Random(3);
            var cpu = new GobbletCpu(new Random(4), depth: 2);
            for (int game = 0; game < 20; game++)
            {
                var s = GobbletState.Initial(game % 2);
                while (!s.Result.IsOver)
                {
                    GobbletMove move;
                    if (s.CurrentPlayer == 1)
                    {
                        move = cpu.ChooseMove(s);
                        Assert.IsTrue(_rules.IsLegal(s, move), move.ToString());
                    }
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
            }
        }

        [Test]
        public void BeatsRandomPlayerMostly()
        {
            var rng = new Random(11);
            int wins = 0;
            const int games = 20;
            for (int game = 0; game < games; game++)
            {
                var cpu = new GobbletCpu(new Random(game));
                var s = GobbletState.Initial(game % 2);
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    var move = s.CurrentPlayer == 1 ? cpu.ChooseMove(s) : moves[rng.Next(moves.Count)];
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 1) wins++;
            }
            Assert.GreaterOrEqual(wins, games - 2);
        }

        [Test]
        public void ChooseMove_ThrowsWhenGameOver()
        {
            var s = Board(0, At(0, A(S)), At(1, A(M)));
            var over = _rules.ApplyMove(s, GobbletMove.Place(L, 2));
            Assert.Throws<InvalidOperationException>(() => new GobbletCpu(new Random(0)).ChooseMove(over));
        }

        // ---------- 対局の進行 ----------

        [Test]
        public void Match_CpuOnlyMovesOnItsTurn()
        {
            var match = new GobbletMatch(new Random(1), cpuPlayer: 1);
            int changes = 0;
            match.StateChanged += _ => changes++;
            match.StartNewGame(firstPlayer: 0);
            Assert.AreEqual(1, changes);
            Assert.IsFalse(match.IsCpuTurn);

            match.PlayCpuTurn();
            Assert.AreEqual(0, match.State.Ply, "人間の手番に CPU は指さない");

            Assert.IsTrue(match.SubmitMove(GobbletMove.Place(L, 4)));
            Assert.IsTrue(match.IsCpuTurn);
            Assert.IsFalse(match.SubmitMove(GobbletMove.Place(S, 0)), "CPU の手番に人間は指せない");

            match.PlayCpuTurn();
            Assert.AreEqual(2, match.State.Ply);
            Assert.AreEqual(3, changes);
        }

        [Test]
        public void Match_RejectsIllegalHumanMove()
        {
            var match = new GobbletMatch(new Random(1), cpuPlayer: 1);
            match.StartNewGame(firstPlayer: 0);
            match.SubmitMove(GobbletMove.Place(S, 4));
            match.PlayCpuTurn();
            Assert.IsFalse(match.SubmitMove(GobbletMove.Move(8, L, 0)));
        }

        [Test]
        public void Match_TwoPlayers_BothSidesAreHuman()
        {
            var match = new GobbletMatch(new Random(1), GobbletMatch.NoCpu);
            match.StartNewGame(firstPlayer: 1);
            Assert.IsTrue(match.IsHumanTurn);
            Assert.IsTrue(match.SubmitMove(GobbletMove.Place(S, 0)));
            Assert.IsTrue(match.IsHumanTurn);
            Assert.IsTrue(match.SubmitMove(GobbletMove.Place(M, 0)));
            Assert.AreEqual(new Piece(0, M), match.State.Top(0));
        }

        [Test]
        public void Match_RandomFirstPlayerUsesBothSides()
        {
            var match = new GobbletMatch(new Random(5), cpuPlayer: 1);
            var seen = new HashSet<int>();
            for (int i = 0; i < 30; i++)
            {
                match.StartNewGame();
                seen.Add(match.State.CurrentPlayer);
            }
            Assert.AreEqual(2, seen.Count);
        }
    }
}
