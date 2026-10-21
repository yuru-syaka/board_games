using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Vanish.Tests
{
    public class VanishRulesTests
    {
        const int E = VanishState.Empty;

        readonly VanishRules _rules = new VanishRules();

        VanishState Play(VanishState s, params int[] cells)
        {
            foreach (int c in cells) s = _rules.ApplyMove(s, VanishMove.Place(c));
            return s;
        }

        // ---------- 初期状態 ----------

        [Test]
        public void Initial_EmptyBoard()
        {
            var s = VanishState.Initial(1);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(0, s.Ply);
            for (int c = 0; c < VanishState.CellCount; c++) Assert.IsTrue(s.IsEmpty(c));
            Assert.AreEqual(0, s.PieceCount(0));
            Assert.AreEqual(0, s.PieceCount(1));
            Assert.IsNull(s.LastMove);
            Assert.AreEqual(E, s.LastVanished);
            Assert.AreEqual(GameStatus.Ongoing, _rules.GetResult(s).Status);
        }

        [Test]
        public void Initial_RejectsInvalidPlayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VanishState.Initial(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => VanishState.Initial(-1));
        }

        [Test]
        public void Initial_AllNineCellsAreLegal()
        {
            var moves = _rules.GetLegalMoves(VanishState.Initial(0));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 9), moves.Select(m => m.Cell));
        }

        [Test]
        public void FromOrder_RejectsInvalidBoards()
        {
            Assert.Throws<ArgumentException>(() => VanishState.FromOrder(new[] { 0, 1, 2, 3 }, null, 0));
            Assert.Throws<ArgumentException>(() => VanishState.FromOrder(new[] { 0 }, new[] { 0 }, 0));
            Assert.Throws<ArgumentException>(() => VanishState.FromOrder(new[] { 9 }, null, 0));
            Assert.Throws<ArgumentException>(() => VanishState.FromOrder(new[] { 1, 1 }, null, 0));
        }

        // ---------- 置く ----------

        [Test]
        public void Place_PutsPieceAndPassesTurn()
        {
            var s = Play(VanishState.Initial(0), 4);
            Assert.AreEqual(0, s.At(4));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(1, s.Ply);
            Assert.AreEqual(VanishMove.Place(4), s.LastMove);
            Assert.AreEqual(E, s.LastVanished);
            CollectionAssert.AreEqual(new[] { 4 }, s.PiecesOf(0).ToArray());
        }

        [Test]
        public void Place_OnOccupiedCell_IsIllegal()
        {
            var s = Play(VanishState.Initial(0), 4);
            Assert.IsFalse(_rules.IsLegal(s, VanishMove.Place(4)));
            Assert.IsFalse(_rules.GetLegalMoves(s).Contains(VanishMove.Place(4)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, VanishMove.Place(4)));
        }

        [Test]
        public void Place_OutOfRange_IsIllegal()
        {
            var s = VanishState.Initial(0);
            Assert.IsFalse(_rules.IsLegal(s, VanishMove.Place(-1)));
            Assert.IsFalse(_rules.IsLegal(s, VanishMove.Place(9)));
        }

        // ---------- 消える ----------

        [Test]
        public void NextToVanish_IsOldestOnlyWhenThreePieces()
        {
            var s = VanishState.FromOrder(new[] { 0, 5 }, new[] { 1, 2, 3 }, 0);
            Assert.AreEqual(E, s.NextToVanish(0));
            Assert.AreEqual(1, s.NextToVanish(1));
        }

        [Test]
        public void FourthPiece_RemovesOldest()
        {
            // P0: 0 → 5 → 7 の順に置いてある(列にならない)
            var s = VanishState.FromOrder(new[] { 0, 5, 7 }, new[] { 1, 4 }, 0);
            var next = _rules.ApplyMove(s, VanishMove.Place(6));
            Assert.IsTrue(next.IsEmpty(0), "oldest piece should vanish");
            Assert.AreEqual(0, next.At(6));
            Assert.AreEqual(0, next.LastVanished);
            CollectionAssert.AreEqual(new[] { 5, 7, 6 }, next.PiecesOf(0).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 4 }, next.PiecesOf(1).ToArray(), "opponent pieces never vanish");
            Assert.AreEqual(3, next.PieceCount(0));
        }

        [Test]
        public void OldestCell_CannotBeChosenBeforePlacing()
        {
            var s = VanishState.FromOrder(new[] { 0, 5, 7 }, new[] { 1, 4 }, 0);
            Assert.IsFalse(_rules.IsLegal(s, VanishMove.Place(0)));
            CollectionAssert.AreEquivalent(new[] { 2, 3, 6, 8 }, _rules.GetLegalMoves(s).Select(m => m.Cell));
        }

        [Test]
        public void PiecesVanishInPlacementOrder()
        {
            // P0 と P1 が交互に置き続け、常に盤上は最大 3 体ずつ
            var s = Play(VanishState.Initial(0), 0, 1, 5, 2, 7, 3);
            Assert.AreEqual(3, s.PieceCount(0));
            Assert.AreEqual(3, s.PieceCount(1));

            s = Play(s, 6); // P0 の 4 体目 → 0 が消える
            Assert.AreEqual(0, s.LastVanished);
            s = Play(s, 8); // P1 の 4 体目 → 1 が消える
            Assert.AreEqual(1, s.LastVanished);
            s = Play(s, 0); // P0 → 5 が消える
            Assert.AreEqual(5, s.LastVanished);
            CollectionAssert.AreEqual(new[] { 7, 6, 0 }, s.PiecesOf(0).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 3, 8 }, s.PiecesOf(1).ToArray());
            Assert.IsFalse(s.Result.IsOver);
        }

        [Test]
        public void ThereAreAlwaysAtLeastThreeEmptyCells()
        {
            var s = VanishState.FromOrder(new[] { 0, 5, 7 }, new[] { 1, 2, 3 }, 0);
            Assert.AreEqual(3, _rules.GetLegalMoves(s).Count);
        }

        // ---------- 勝敗 ----------

        [Test]
        public void ThreeInARow_Wins()
        {
            var s = Play(VanishState.Initial(0), 0, 3, 1, 4, 2);
            Assert.AreEqual(GameResult.WinFor(0), s.Result);
            Assert.AreEqual(EndReason.Line, s.EndReason);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, s.WinningLine.ToArray());
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
            Assert.IsFalse(_rules.IsLegal(s, VanishMove.Place(5)));
        }

        [Test]
        public void AllLines_AreDetected()
        {
            foreach (var line in VanishState.Lines)
            {
                var s = VanishState.FromOrder(new[] { line[0], line[1] }, null, 0);
                var next = _rules.ApplyMove(s, VanishMove.Place(line[2]));
                Assert.AreEqual(GameResult.WinFor(0), next.Result, string.Join(",", line));
            }
        }

        [Test]
        public void DiagonalWin_ForPlayer1()
        {
            var s = VanishState.FromOrder(new[] { 0, 1 }, new[] { 2, 4 }, 1);
            var next = _rules.ApplyMove(s, VanishMove.Place(6));
            Assert.AreEqual(GameResult.WinFor(1), next.Result);
            CollectionAssert.AreEqual(new[] { 2, 4, 6 }, next.WinningLine.ToArray());
        }

        [Test]
        public void VanishedPiece_DoesNotCountForLine()
        {
            // P0 は 0(最古)・1・8。2 に置くと 0 が消えるので 0-1-2 はそろわない
            var s = VanishState.FromOrder(new[] { 0, 1, 8 }, new[] { 3, 4 }, 0);
            var next = _rules.ApplyMove(s, VanishMove.Place(2));
            Assert.IsFalse(next.Result.IsOver);
            Assert.IsNull(next.WinningLine);
            Assert.IsTrue(next.IsEmpty(0));
        }

        [Test]
        public void LineFromRemainingPieces_WinsEvenAfterVanish()
        {
            // P0 は 8(最古)・0・1。2 に置くと 8 が消え、0-1-2 がそろう
            var s = VanishState.FromOrder(new[] { 8, 0, 1 }, new[] { 3, 4 }, 0);
            var next = _rules.ApplyMove(s, VanishMove.Place(2));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
            Assert.AreEqual(8, next.LastVanished);
        }

        [Test]
        public void TurnLimit_IsDraw()
        {
            var s = VanishState.FromOrder(new[] { 0 }, new[] { 4 }, 0, VanishState.MaxPlies - 1);
            var next = _rules.ApplyMove(s, VanishMove.Place(8));
            Assert.AreEqual(GameStatus.Draw, next.Result.Status);
            Assert.AreEqual(EndReason.TurnLimit, next.EndReason);
            Assert.AreEqual(VanishState.MaxPlies, next.Ply);
        }

        [Test]
        public void WinOnLastPly_BeatsTurnLimit()
        {
            var s = VanishState.FromOrder(new[] { 0, 1 }, new[] { 4 }, 0, VanishState.MaxPlies - 1);
            var next = _rules.ApplyMove(s, VanishMove.Place(2));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
        }

        [Test]
        public void RandomGames_AlwaysEndWithinTurnLimit()
        {
            var rng = new Random(42);
            for (int game = 0; game < 50; game++)
            {
                var s = VanishState.Initial(rng.Next(2));
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.GreaterOrEqual(moves.Count, 3);
                    Assert.LessOrEqual(s.PieceCount(0), 3);
                    Assert.LessOrEqual(s.PieceCount(1), 3);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
                Assert.LessOrEqual(s.Ply, VanishState.MaxPlies);
            }
        }
    }
}
