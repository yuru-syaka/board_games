using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Quarto.Tests
{
    public class QuartoCpuTests
    {
        const int E = QuartoBoard.Empty;

        readonly QuartoRules _rules = new QuartoRules();

        static int[] EmptyCells() => Enumerable.Repeat(E, QuartoBoard.CellCount).ToArray();

        [Test]
        public void Cpu_AlwaysReturnsLegalMove()
        {
            var rng = new Random(5);
            for (int game = 0; game < 10; game++)
            {
                var cpu = new QuartoCpu(new Random(game));
                var s = QuartoSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    QuartoMove move;
                    if (s.CurrentPlayer == 1)
                    {
                        move = cpu.ChooseMove(s);
                        Assert.IsTrue(_rules.IsLegal(s, move), $"illegal CPU move {move}");
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
        public void Cpu_PlacesWinningPiece()
        {
            var cells = EmptyCells();
            cells[QuartoBoard.Index(0, 2)] = 1;
            cells[QuartoBoard.Index(1, 2)] = 3;
            cells[QuartoBoard.Index(3, 2)] = 5;
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, 7);
            for (int seed = 0; seed < 10; seed++)
                Assert.AreEqual(QuartoMove.Place(2, 2), new QuartoCpu(new Random(seed)).ChooseMove(s));
        }

        [Test]
        public void Cpu_DoesNotGiveImmediatelyWinningPiece()
        {
            // 横 y=0 に Tall(ビット 1)の駒が 3 つ: Tall の駒を渡すと相手がそろえられる
            var cells = EmptyCells();
            cells[0] = 1;
            cells[1] = 3;
            cells[2] = 5;
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Give);
            for (int seed = 0; seed < 20; seed++)
            {
                var move = new QuartoCpu(new Random(seed)).ChooseMove(s);
                Assert.IsTrue(move.IsGive);
                Assert.IsFalse(QuartoPiece.Has(move.Piece, QuartoTrait.Tall), $"gave dangerous piece #{move.Piece}");
            }
        }

        [Test]
        public void Cpu_AvoidsPlacementThatLeavesOnlyLosingGives()
        {
            // y=1 に 1,3 がある。手もちは 5(Tall)。
            // (2,1) か (3,1) に置くと y=1 が Tall 3 つになり、残りの Tall 駒は全て危険になる。
            // どこに置いても安全な駒は残るので、置いた後に相手がすぐ勝てない駒を渡すことを確認する。
            var cells = EmptyCells();
            cells[QuartoBoard.Index(0, 1)] = 1;
            cells[QuartoBoard.Index(1, 1)] = 3;
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, 5);
            for (int seed = 0; seed < 10; seed++)
            {
                var cpu = new QuartoCpu(new Random(seed));
                var place = cpu.ChooseMove(s);
                var after = _rules.ApplyMove(s, place);
                var give = cpu.ChooseMove(after);
                Assert.IsTrue(give.IsGive);
                var given = _rules.ApplyMove(after, give);
                foreach (var reply in _rules.GetLegalMoves(given))
                    Assert.AreNotEqual(GameStatus.Win, _rules.ApplyMove(given, reply).Result.Status,
                        $"seed {seed}: {place} then {give} lets the opponent win with {reply}");
            }
        }

        [Test]
        public void Cpu_GivesThePiecePlannedWhenPlacing()
        {
            var rng = new Random(3);
            var cpu = new QuartoCpu(new Random(7));
            var s = QuartoSetup.Create(0);
            s = _rules.ApplyMove(s, QuartoMove.Give(0));
            for (int turn = 0; turn < 5 && !s.Result.IsOver; turn++)
            {
                s = _rules.ApplyMove(s, cpu.ChooseMove(s)); // CPU が置く
                if (s.Result.IsOver) break;
                var give = cpu.ChooseMove(s);
                Assert.IsTrue(_rules.IsLegal(s, give));
                s = _rules.ApplyMove(s, give);
                // 相手(ランダム)
                var moves = _rules.GetLegalMoves(s);
                s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                if (s.Result.IsOver) break;
                moves = _rules.GetLegalMoves(s);
                s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
            }
        }

        [Test]
        public void Cpu_BeatsRandomPlayerMostOfTheTime()
        {
            var rng = new Random(21);
            int cpuWins = 0, cpuLosses = 0;
            const int games = 20;
            for (int game = 0; game < games; game++)
            {
                var cpu = new QuartoCpu(new Random(game + 100));
                var s = QuartoSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    QuartoMove move;
                    if (s.CurrentPlayer == 1) move = cpu.ChooseMove(s);
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 1) cpuWins++;
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 0) cpuLosses++;
            }
            Assert.GreaterOrEqual(cpuWins, 15, $"wins {cpuWins}, losses {cpuLosses}");
            Assert.LessOrEqual(cpuLosses, 1, $"wins {cpuWins}, losses {cpuLosses}");
        }

        [Test]
        public void CpuVsCpu_PlaysToTheEnd()
        {
            for (int game = 0; game < 3; game++)
            {
                var cpus = new[] { new QuartoCpu(new Random(game)), new QuartoCpu(new Random(game + 50)) };
                var s = QuartoSetup.Create(game % 2);
                while (!s.Result.IsOver)
                {
                    var move = cpus[s.CurrentPlayer].ChooseMove(s);
                    Assert.IsTrue(_rules.IsLegal(s, move));
                    s = _rules.ApplyMove(s, move);
                }
                Assert.IsTrue(s.Result.IsOver);
            }
        }

        [Test]
        public void Cpu_IsDeterministicForSameSeed()
        {
            var s = _rules.ApplyMove(QuartoSetup.Create(0), QuartoMove.Give(9));
            s = _rules.ApplyMove(s, QuartoMove.Place(1, 1));
            s = _rules.ApplyMove(s, QuartoMove.Give(6));
            var a = new QuartoCpu(new Random(4)).ChooseMove(s);
            var b = new QuartoCpu(new Random(4)).ChooseMove(s);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ChooseMove_ThrowsWhenGameIsOver()
        {
            var cells = EmptyCells();
            cells[0] = 1;
            cells[1] = 3;
            cells[2] = 5;
            var s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 7), QuartoMove.Place(3, 0));
            Assert.Throws<InvalidOperationException>(() => new QuartoCpu(new Random(1)).ChooseMove(s));
        }
    }
}
