using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Quarto.Tests
{
    public class QuartoRulesTests
    {
        const int E = QuartoBoard.Empty;

        readonly QuartoRules _rules = new QuartoRules();

        static int[] EmptyCells() => Enumerable.Repeat(E, QuartoBoard.CellCount).ToArray();

        /// <summary>行 y を上から順に並べた見た目どおりの配列から盤を作る(rows[0] が y=3)。</summary>
        static int[] Board(params int[][] rows)
        {
            var cells = EmptyCells();
            for (int r = 0; r < 4; r++)
            for (int x = 0; x < 4; x++)
                cells[QuartoBoard.Index(x, 3 - r)] = rows[r][x];
            return cells;
        }

        // ---------- 初期状態 ----------

        [Test]
        public void Setup_StartsWithEmptyBoardAndGivePhase()
        {
            var s = QuartoSetup.Create(1);
            Assert.AreEqual(16, s.Cells.Count);
            Assert.IsTrue(s.Cells.All(c => c == E));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Give, s.Phase);
            Assert.AreEqual(-1, s.PieceInHand);
            Assert.AreEqual(0, s.Ply);
            Assert.AreEqual(16, s.AvailablePieces().Count);
            Assert.AreEqual(GameStatus.Ongoing, _rules.GetResult(s).Status);
        }

        [Test]
        public void Setup_RejectsInvalidFirstPlayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QuartoSetup.Create(2));
        }

        [Test]
        public void Pieces_CoverAllTraitCombinations()
        {
            for (int p = 0; p < 16; p++)
            {
                int rebuilt = QuartoPiece.Create(QuartoPiece.Has(p, QuartoTrait.Tall), QuartoPiece.Has(p, QuartoTrait.Pink),
                    QuartoPiece.Has(p, QuartoTrait.Square), QuartoPiece.Has(p, QuartoTrait.Hollow));
                Assert.AreEqual(p, rebuilt);
            }
        }

        [Test]
        public void Board_HasTenLines()
        {
            Assert.AreEqual(10, QuartoBoard.Lines.Length);
            // 角は 3 本(横・縦・斜め)、中央寄りの斜め上のマスも 3 本、それ以外は 2 本
            Assert.AreEqual(3, QuartoBoard.LinesThroughCell[QuartoBoard.Index(0, 0)].Length);
            Assert.AreEqual(3, QuartoBoard.LinesThroughCell[QuartoBoard.Index(1, 1)].Length);
            Assert.AreEqual(2, QuartoBoard.LinesThroughCell[QuartoBoard.Index(1, 0)].Length);
        }

        // ---------- 合法手 ----------

        [Test]
        public void GivePhase_LegalMovesAreAllAvailablePieces()
        {
            var cells = EmptyCells();
            cells[0] = 3;
            cells[5] = 7;
            var s = QuartoSetup.CreateCustom(cells, 0, TurnPhase.Give);
            var moves = _rules.GetLegalMoves(s);
            Assert.AreEqual(14, moves.Count);
            Assert.IsTrue(moves.All(m => m.IsGive));
            Assert.IsFalse(moves.Any(m => m.Piece == 3 || m.Piece == 7));
        }

        [Test]
        public void PlacePhase_LegalMovesAreAllEmptyCells()
        {
            var cells = EmptyCells();
            cells[0] = 3;
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, 9);
            var moves = _rules.GetLegalMoves(s);
            Assert.AreEqual(15, moves.Count);
            Assert.IsTrue(moves.All(m => m.IsPlace && m.Cell != 0));
        }

        [Test]
        public void CannotGivePieceAlreadyUsedOrInHand()
        {
            var cells = EmptyCells();
            cells[0] = 3;
            var s = QuartoSetup.CreateCustom(cells, 0, TurnPhase.Give);
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Give(3)));
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Give(16)));
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Give(-1)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, QuartoMove.Give(3)));
        }

        [Test]
        public void CannotPlaceOnOccupiedCellOrOffBoard()
        {
            var cells = EmptyCells();
            cells[QuartoBoard.Index(2, 1)] = 3;
            var s = QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 5);
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Place(2, 1)));
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Place(4, 0)));
            Assert.IsFalse(_rules.IsLegal(s, QuartoMove.Place(-1, 0)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, QuartoMove.Place(2, 1)));
        }

        [Test]
        public void WrongActionForPhase_IsIllegal()
        {
            var give = QuartoSetup.Create(0);
            Assert.IsFalse(_rules.IsLegal(give, QuartoMove.Place(0, 0)));
            var place = _rules.ApplyMove(give, QuartoMove.Give(4));
            Assert.IsFalse(_rules.IsLegal(place, QuartoMove.Give(5)));
        }

        // ---------- 進行 ----------

        [Test]
        public void Give_PassesTurnAndPieceToOpponent()
        {
            var s = _rules.ApplyMove(QuartoSetup.Create(0), QuartoMove.Give(6));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Place, s.Phase);
            Assert.AreEqual(6, s.PieceInHand);
            Assert.AreEqual(1, s.Ply);
            Assert.IsFalse(s.IsAvailable(6));
            Assert.AreEqual(QuartoMove.Give(6), s.LastMove);
        }

        [Test]
        public void Place_KeepsTurnAndAsksSamePlayerToGive()
        {
            var s = _rules.ApplyMove(QuartoSetup.Create(0), QuartoMove.Give(6));
            s = _rules.ApplyMove(s, QuartoMove.Place(1, 2));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Give, s.Phase);
            Assert.AreEqual(-1, s.PieceInHand);
            Assert.AreEqual(6, s.PieceAt(1, 2));
            Assert.AreEqual(2, s.Ply);
            Assert.AreEqual(15, s.AvailablePieces().Count);
        }

        [Test]
        public void ApplyMove_DoesNotMutateOriginal()
        {
            var s0 = _rules.ApplyMove(QuartoSetup.Create(0), QuartoMove.Give(6));
            _rules.ApplyMove(s0, QuartoMove.Place(0, 0));
            Assert.AreEqual(E, s0.PieceAt(0, 0));
            Assert.AreEqual(6, s0.PieceInHand);
        }

        // ---------- 勝敗 ----------

        [Test]
        public void Quarto_RowSharingTrait_PlacerWins()
        {
            // 横一列(y=0)に 1,3,5 があり、7 を置く: 全て Tall(ビット 1)を共有
            var cells = Board(
                new[] { E, E, E, E },
                new[] { E, E, E, E },
                new[] { E, E, E, E },
                new[] { 1, 3, 5, E });
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, 7);
            s = _rules.ApplyMove(s, QuartoMove.Place(3, 0));
            Assert.AreEqual(GameStatus.Win, s.Result.Status);
            Assert.AreEqual(1, s.Result.Winner);
            Assert.AreEqual(EndReason.Quarto, s.EndReason);
            CollectionAssert.AreEqual(new[] { 0 }, s.WinningLines.ToArray());
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void Quarto_SharedZeroTrait_AlsoWins()
        {
            // 縦一列(x=2)に 0,2,4: ビット 1(Tall)とビット 8(Hollow)が全て 0 → 6 を置いてそろう
            var cells = EmptyCells();
            cells[QuartoBoard.Index(2, 0)] = 0;
            cells[QuartoBoard.Index(2, 1)] = 2;
            cells[QuartoBoard.Index(2, 2)] = 4;
            var s = QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 6);
            s = _rules.ApplyMove(s, QuartoMove.Place(2, 3));
            Assert.AreEqual(GameStatus.Win, s.Result.Status);
            Assert.AreEqual(0, s.Result.Winner);
            CollectionAssert.AreEqual(new[] { 6 }, s.WinningLines.ToArray());
        }

        [Test]
        public void Quarto_BothDiagonals()
        {
            // 斜め(左下→右上)
            var cells = EmptyCells();
            cells[QuartoBoard.Index(0, 0)] = 8;
            cells[QuartoBoard.Index(1, 1)] = 9;
            cells[QuartoBoard.Index(2, 2)] = 10;
            var s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 11), QuartoMove.Place(3, 3));
            CollectionAssert.AreEqual(new[] { 8 }, s.WinningLines.ToArray());

            // 斜め(左上→右下)
            cells = EmptyCells();
            cells[QuartoBoard.Index(0, 3)] = 2;
            cells[QuartoBoard.Index(1, 2)] = 3;
            cells[QuartoBoard.Index(2, 1)] = 6;
            s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, 7), QuartoMove.Place(3, 0));
            CollectionAssert.AreEqual(new[] { 9 }, s.WinningLines.ToArray());
            Assert.AreEqual(1, s.Result.Winner);
        }

        [Test]
        public void FourPiecesWithNoSharedTrait_DoNotWin()
        {
            // 0(0000), 15(1111), 3(0011), 12(1100): どのビットも共有しない
            var cells = EmptyCells();
            cells[0] = 0;
            cells[1] = 15;
            cells[2] = 3;
            var s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 12), QuartoMove.Place(3, 0));
            Assert.AreEqual(GameStatus.Ongoing, s.Result.Status);
            Assert.AreEqual(TurnPhase.Give, s.Phase);
            Assert.AreEqual(0, s.WinningLines.Count);
        }

        [Test]
        public void ThreeInLine_DoesNotWin()
        {
            var cells = EmptyCells();
            cells[0] = 1;
            cells[1] = 3;
            var s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 5), QuartoMove.Place(2, 0));
            Assert.AreEqual(GameStatus.Ongoing, s.Result.Status);
        }

        [Test]
        public void Quarto_TwoLinesAtOnce_AreBothReported()
        {
            // (3,0) に置くと 横 y=0 と 縦 x=3 が同時にそろう(全て Pink = ビット 2)
            var cells = EmptyCells();
            cells[QuartoBoard.Index(0, 0)] = 2;
            cells[QuartoBoard.Index(1, 0)] = 3;
            cells[QuartoBoard.Index(2, 0)] = 6;
            cells[QuartoBoard.Index(3, 1)] = 7;
            cells[QuartoBoard.Index(3, 2)] = 10;
            cells[QuartoBoard.Index(3, 3)] = 11;
            var s = _rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 14), QuartoMove.Place(3, 0));
            CollectionAssert.AreEqual(new[] { 0, 7 }, s.WinningLines.ToArray());
        }

        [Test]
        public void LastPieceWithoutQuarto_IsDraw()
        {
            // どのラインも特徴を共有しない並び(各行に補数の組を 2 つずつ置く)
            var full = Board(
                new[] { 0, 15, 5, 10 },
                new[] { 3, 12, 6, 9 },
                new[] { 2, 13, 8, 7 },
                new[] { 14, 1, 4, 11 });
            Assert.AreEqual(0, QuartoBoard.FindWinningLines(full).Count, "test board must have no quarto");

            int lastCell = QuartoBoard.Index(3, 0);
            int lastPiece = full[lastCell];
            var cells = (int[])full.Clone();
            cells[lastCell] = E;
            var s = QuartoSetup.CreateCustom(cells, 1, TurnPhase.Place, lastPiece);
            Assert.AreEqual(31, s.Ply);
            s = _rules.ApplyMove(s, QuartoMove.Place(3, 0));
            Assert.AreEqual(GameStatus.Draw, s.Result.Status);
            Assert.AreEqual(EndReason.BoardFull, s.EndReason);
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void CreateCustom_RejectsInconsistentPositions()
        {
            var cells = EmptyCells();
            cells[0] = 3;
            cells[1] = 3;
            Assert.Throws<ArgumentException>(() => QuartoSetup.CreateCustom(cells, 0, TurnPhase.Give));

            cells = EmptyCells();
            cells[0] = 3;
            Assert.Throws<ArgumentException>(() => QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 3));
            Assert.Throws<ArgumentException>(() => QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, -1));
            Assert.Throws<ArgumentException>(() => QuartoSetup.CreateCustom(cells, 0, TurnPhase.Give, 4));
        }

        [Test]
        public void RandomGames_AlwaysEndWithinSixteenPlacements()
        {
            var rng = new Random(11);
            for (int game = 0; game < 200; game++)
            {
                var s = QuartoSetup.CreateRandom(rng);
                int guard = 0;
                while (!s.Result.IsOver && guard++ < 40)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.IsNotEmpty(moves);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
                Assert.IsTrue(s.Result.IsOver);
                Assert.LessOrEqual(s.Ply, 32);
                if (s.Result.Status == GameStatus.Win)
                {
                    Assert.AreEqual(EndReason.Quarto, s.EndReason);
                    Assert.IsNotEmpty(s.WinningLines);
                    // 勝者は最後に置いたプレイヤー
                    Assert.IsTrue(s.LastMove.Value.IsPlace);
                    Assert.AreEqual(s.CurrentPlayer, s.Result.Winner);
                }
                else
                {
                    Assert.AreEqual(16, s.PlacedCount);
                }
            }
        }
    }
}
