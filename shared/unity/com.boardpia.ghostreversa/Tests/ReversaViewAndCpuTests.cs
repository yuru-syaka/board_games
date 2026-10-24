using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostReversa.Tests
{
    public class ReversaViewAndCpuTests
    {
        readonly ReversaRules _rules = new ReversaRules();

        static ReversaState Custom(int current, Tile?[] cells, int b0, int r0, int b1, int r1, TileKind d0, TileKind d1) =>
            ReversaSetup.CreateCustom(cells, new[] { b0, b1 }, new[] { r0, r1 }, new[] { d0, d1 }, 0, current, 10);

        // ---------- 視点(隠し情報) ----------

        [Test]
        public void View_HidesOpponentFaceDownColors_HandBreakdown_AndDiscard()
        {
            var cells = new Tile?[9];
            cells[0] = new Tile(1, TileKind.Red, false);
            cells[1] = new Tile(1, TileKind.Blue, true);
            cells[2] = new Tile(0, TileKind.Red, false);
            var s = Custom(0, cells, 2, 1, 1, 2, TileKind.Blue, TileKind.Red);
            var v = ReversaPlayerView.Create(s, 0);

            Assert.AreEqual(new Tile(1, TileKind.Unknown, false), v.Cells[0], "相手の裏の駒は色が見えない(持ち主は見える)");
            Assert.AreEqual(new Tile(1, TileKind.Blue, true), v.Cells[1], "表の駒は見える");
            Assert.AreEqual(new Tile(0, TileKind.Red, false), v.Cells[2], "自分の裏の駒は見える");
            Assert.AreEqual(ReversaPlayerView.Hidden, v.HandOf(1, TileKind.Blue));
            Assert.AreEqual(ReversaPlayerView.Hidden, v.HandOf(1, TileKind.Red));
            Assert.AreEqual(3, v.HandCount(1), "手駒の合計は見える");
            Assert.AreEqual(2, v.HandOf(0, TileKind.Blue));
            Assert.AreEqual(TileKind.Unknown, v.DiscardOf(1));
            Assert.AreEqual(TileKind.Blue, v.DiscardOf(0));
        }

        [Test]
        public void RevealedView_ShowsEverything()
        {
            var cells = new Tile?[9];
            cells[0] = new Tile(1, TileKind.Red, false);
            var s = Custom(0, cells, 2, 1, 1, 2, TileKind.Blue, TileKind.Red);
            var v = ReversaPlayerView.CreateRevealed(s, 0);
            Assert.AreEqual(TileKind.Red, v.Cells[0].Value.Kind);
            Assert.AreEqual(2, v.HandOf(1, TileKind.Red));
            Assert.AreEqual(TileKind.Red, v.DiscardOf(1));
        }

        [Test]
        public void View_HidesOpponentsLastMoveColor_WhenDiscardOrFaceDown()
        {
            var s = ReversaSetup.Create(1);
            s = _rules.ApplyMove(s, ReversaMove.Discard(TileKind.Blue)); // P1 が捨てる
            Assert.AreEqual(TileKind.Unknown, ReversaPlayerView.Create(s, 0).LastMove.Value.Piece);
            Assert.AreEqual(TileKind.Blue, ReversaPlayerView.Create(s, 1).LastMove.Value.Piece, "自分の手は見える");

            s = _rules.ApplyMove(s, ReversaMove.Discard(TileKind.Red)); // P0
            s = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Red, 1, 1, false)); // P1 が裏で置く
            var seen = ReversaPlayerView.Create(s, 0).LastMove.Value;
            Assert.AreEqual(TileKind.Unknown, seen.Piece);
            Assert.AreEqual(1, seen.X);

            s = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 0, 0, true)); // P0 が表で置く
            Assert.AreEqual(TileKind.Blue, ReversaPlayerView.Create(s, 1).LastMove.Value.Piece, "表で置いた手は見える");
        }

        [Test]
        public void ViewLegalMoves_MatchTheFullState()
        {
            var rng = new Random(11);
            for (int game = 0; game < 50; game++)
            {
                var s = ReversaSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    var full = _rules.GetLegalMoves(s);
                    var fromView = ReversaPlayerView.Create(s, s.CurrentPlayer).GetLegalMoves();
                    CollectionAssert.AreEquivalent(full, fromView);
                    Assert.IsEmpty(ReversaPlayerView.Create(s, 1 - s.CurrentPlayer).GetLegalMoves());
                    s = _rules.ApplyMove(s, full[rng.Next(full.Count)]);
                }
            }
        }

        // ---------- CPU ----------

        [Test]
        public void Cpu_TakesAnImmediateWin()
        {
            var cells = new Tile?[9];
            cells[ReversaBoard.Index(0, 2)] = new Tile(1, TileKind.Blue, true);
            cells[ReversaBoard.Index(2, 2)] = new Tile(1, TileKind.Blue, true);
            var s = Custom(1, cells, 2, 2, 1, 2, TileKind.Red, TileKind.Red);
            for (int seed = 0; seed < 5; seed++)
            {
                var move = new ReversaCpu(new Random(seed)).ChooseMove(ReversaPlayerView.Create(s, 1));
                var next = _rules.ApplyMove(s, move);
                Assert.AreEqual(1, next.Result.Winner, $"seed {seed}: {move}");
            }
        }

        [Test]
        public void Cpu_AvoidsGivingTheOpponentTheWin()
        {
            // P0 の青が 2 体表、1 体が (1,0) に裏。CPU(P1)が行 y=0 を表の青で裏返すと P0 が勝つ
            var cells = new Tile?[9];
            cells[ReversaBoard.Index(0, 2)] = new Tile(0, TileKind.Blue, true);
            cells[ReversaBoard.Index(2, 2)] = new Tile(0, TileKind.Blue, true);
            cells[ReversaBoard.Index(1, 0)] = new Tile(0, TileKind.Blue, false);
            var s = Custom(1, cells, 1, 2, 2, 2, TileKind.Red, TileKind.Red);
            for (int seed = 0; seed < 5; seed++)
            {
                var move = new ReversaCpu(new Random(seed)).ChooseMove(ReversaPlayerView.Create(s, 1));
                var next = _rules.ApplyMove(s, move);
                Assert.AreNotEqual(0, next.Result.Winner, $"seed {seed}: {move}");
            }
        }

        [Test]
        public void Cpu_AlwaysReturnsALegalMove_AndBeatsRandomMostly()
        {
            var rng = new Random(5);
            int cpuWins = 0, randomWins = 0;
            for (int game = 0; game < 30; game++)
            {
                var cpu = new ReversaCpu(new Random(game));
                int cpuSeat = game % 2;
                var s = ReversaSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    ReversaMove move;
                    if (s.CurrentPlayer == cpuSeat)
                    {
                        move = cpu.ChooseMove(ReversaPlayerView.Create(s, cpuSeat));
                        Assert.IsTrue(_rules.IsLegal(s, move), move.ToString());
                    }
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win)
                {
                    if (s.Result.Winner == cpuSeat) cpuWins++;
                    else randomWins++;
                }
            }
            Assert.Greater(cpuWins, randomWins * 2, $"CPU {cpuWins} - random {randomWins}");
        }

        [Test]
        public void Determinize_IsConsistentWithTheView()
        {
            var rng = new Random(3);
            var cpu = new ReversaCpu(new Random(4));
            for (int game = 0; game < 40; game++)
            {
                var s = ReversaSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    if (s.Phase == GamePhase.Play)
                    {
                        int me = s.CurrentPlayer, opp = 1 - me;
                        var world = cpu.Determinize(ReversaPlayerView.Create(s, me));
                        Assert.AreEqual(s.HandCount(opp), world.HandCount(opp));
                        for (int i = 0; i < 9; i++)
                        {
                            Assert.AreEqual(s.Cells[i].HasValue, world.Cells[i].HasValue);
                            if (!s.Cells[i].HasValue) continue;
                            Assert.AreEqual(s.Cells[i].Value.Owner, world.Cells[i].Value.Owner);
                            Assert.AreEqual(s.Cells[i].Value.FaceUp, world.Cells[i].Value.FaceUp);
                            if (s.Cells[i].Value.FaceUp || s.Cells[i].Value.Owner == me)
                                Assert.AreEqual(s.Cells[i].Value.Kind, world.Cells[i].Value.Kind);
                            else
                                Assert.IsTrue(ReversaBoard.IsColor(world.Cells[i].Value.Kind));
                        }
                        CollectionAssert.AreEquivalent(_rules.GetLegalMoves(s), _rules.GetLegalMoves(world));
                    }
                    var moves = _rules.GetLegalMoves(s);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
            }
        }
    }
}
