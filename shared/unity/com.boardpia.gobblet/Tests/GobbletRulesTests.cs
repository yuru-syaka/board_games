using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Gobblet.Tests
{
    public class GobbletRulesTests
    {
        const PieceSize S = PieceSize.Small;
        const PieceSize M = PieceSize.Medium;
        const PieceSize L = PieceSize.Large;

        readonly GobbletRules _rules = new GobbletRules();

        static Piece A(PieceSize size) => new Piece(0, size);
        static Piece B(PieceSize size) => new Piece(1, size);

        /// <summary>マス番号 → 下から上の駒 の指定で盤面を作る。</summary>
        static GobbletState Board(int current, params (int cell, Piece[] stack)[] cells)
        {
            var stacks = new IReadOnlyList<Piece>[GobbletState.CellCount];
            foreach (var (cell, stack) in cells) stacks[cell] = stack;
            return GobbletState.FromBoard(stacks, current);
        }

        static (int, Piece[]) At(int cell, params Piece[] stack) => (cell, stack);

        // ---------- 初期状態 ----------

        [Test]
        public void Initial_EmptyBoardAndFullHands()
        {
            var s = GobbletState.Initial(1);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(0, s.Ply);
            for (int c = 0; c < GobbletState.CellCount; c++) Assert.IsFalse(s.Top(c).HasValue);
            foreach (PieceSize z in Enum.GetValues(typeof(PieceSize)))
            {
                Assert.AreEqual(2, s.HandCount(0, z));
                Assert.AreEqual(2, s.HandCount(1, z));
            }
            Assert.AreEqual(GameStatus.Ongoing, _rules.GetResult(s).Status);
        }

        [Test]
        public void Initial_RejectsInvalidPlayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GobbletState.Initial(2));
        }

        [Test]
        public void Initial_HasPlacementMovesOnly()
        {
            var moves = _rules.GetLegalMoves(GobbletState.Initial(0));
            Assert.AreEqual(3 * 9, moves.Count);
            Assert.IsTrue(moves.All(m => m.IsFromHand));
        }

        [Test]
        public void FromBoard_RejectsSmallerOnTopOrTooManyPieces()
        {
            Assert.Throws<ArgumentException>(() => Board(0, At(0, A(L), B(S))));
            Assert.Throws<ArgumentException>(() => Board(0, At(0, A(M), B(M))));
            Assert.Throws<ArgumentException>(() => Board(0, At(0, A(S)), At(1, A(S)), At(2, A(S))));
        }

        // ---------- 合法手 ----------

        [Test]
        public void Place_OnEmptyCell_ConsumesHand()
        {
            var s = _rules.ApplyMove(GobbletState.Initial(0), GobbletMove.Place(M, 4));
            Assert.AreEqual(A(M), s.Top(4));
            Assert.AreEqual(1, s.HandCount(0, M));
            Assert.AreEqual(2, s.HandCount(1, M));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(1, s.Ply);
            Assert.AreEqual(GobbletMove.Place(M, 4), s.LastMove);
        }

        [Test]
        public void Place_CanGobbleOnlyStrictlySmallerPiece()
        {
            var s = Board(0, At(0, B(S)), At(1, B(M)), At(2, B(L)));
            Assert.IsTrue(_rules.IsLegal(s, GobbletMove.Place(M, 0)));
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Place(M, 1)), "同じ大きさにはかぶせられない");
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Place(L, 2)), "大には何もかぶせられない");
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Place(S, 0)));
        }

        [Test]
        public void Place_CanGobbleOwnPiece()
        {
            var s = Board(0, At(4, A(S)));
            var next = _rules.ApplyMove(s, GobbletMove.Place(L, 4));
            CollectionAssert.AreEqual(new[] { A(S), A(L) }, next.GetStack(4));
        }

        [Test]
        public void Place_NotAllowedWhenHandEmpty()
        {
            var s = Board(0, At(0, A(L)), At(1, A(L)));
            Assert.AreEqual(0, s.HandCount(0, L));
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Place(L, 4)));
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.IsFromHand && m.Size == L));
        }

        [Test]
        public void Move_OwnTopPieceToEmptyOrSmaller()
        {
            var s = Board(0, At(0, A(M)), At(1, B(S)), At(2, B(M)));
            Assert.IsTrue(_rules.IsLegal(s, GobbletMove.Move(0, M, 1)));
            Assert.IsTrue(_rules.IsLegal(s, GobbletMove.Move(0, M, 8)));
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Move(0, M, 2)), "同じ大きさにはかぶせられない");
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Move(0, M, 0)), "同じマスへは動かせない");
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Move(1, S, 5)), "相手の駒は動かせない");
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Move(0, L, 5)), "大きさが違う");
        }

        [Test]
        public void Move_CannotMoveCoveredPiece()
        {
            var s = Board(0, At(0, A(S), B(M)));
            Assert.IsFalse(_rules.IsLegal(s, GobbletMove.Move(0, S, 4)));
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => !m.IsFromHand));
        }

        [Test]
        public void Move_LiftingRevealsPieceUnderneath()
        {
            var s = Board(1, At(0, A(S), B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Move(0, M, 4));
            Assert.AreEqual(A(S), next.Top(0));
            Assert.AreEqual(B(M), next.Top(4));
            Assert.AreEqual(1, next.GetStack(0).Count);
            Assert.AreEqual(1, next.HandCount(1, M), "盤上の駒を動かしても手持ちは変わらない");
        }

        [Test]
        public void ApplyMove_ThrowsOnIllegalMove()
        {
            var s = Board(0, At(0, B(L)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GobbletMove.Place(L, 0)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GobbletMove.Place(S, 9)));
        }

        [Test]
        public void LegalMoves_AllPassIsLegalAndNoDuplicates()
        {
            var s = Board(0, At(0, A(S), B(M)), At(4, A(L)), At(8, B(S)));
            var moves = _rules.GetLegalMoves(s);
            Assert.AreEqual(moves.Count, moves.Distinct().Count());
            foreach (var m in moves) Assert.IsTrue(_rules.IsLegal(s, m), m.ToString());
        }

        // ---------- 勝敗 ----------

        [Test]
        public void Win_ThreeInARow()
        {
            var s = Board(0, At(0, A(S)), At(1, A(M)), At(3, B(S)), At(4, B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Place(S, 2));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
            Assert.AreEqual(EndReason.Line, next.EndReason);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, next.WinningLine);
            Assert.AreEqual(0, _rules.GetLegalMoves(next).Count);
        }

        [TestCase(2, 4, 6)]
        [TestCase(0, 4, 8)]
        [TestCase(1, 4, 7)]
        [TestCase(6, 7, 8)]
        public void Win_AllLineDirections(int a, int b, int c)
        {
            var s = Board(1, At(a, B(S)), At(b, B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Place(L, c));
            Assert.AreEqual(GameResult.WinFor(1), next.Result);
            CollectionAssert.AreEquivalent(new[] { a, b, c }, next.WinningLine);
        }

        [Test]
        public void Win_ByGobblingIntoLine()
        {
            var s = Board(0, At(0, A(S)), At(1, A(S)), At(2, B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Place(L, 2));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
        }

        [Test]
        public void CoveredPiecesDoNotCount()
        {
            // 0,1 は A だが 2 は A の上に B がかぶっている
            var s = Board(1, At(0, A(S)), At(1, A(S)), At(2, A(M), B(L)));
            Assert.IsNull(s.FindLine(0));
            var next = _rules.ApplyMove(s, GobbletMove.Place(S, 8));
            Assert.IsFalse(next.Result.IsOver);
        }

        [Test]
        public void RevealedOpponentLine_MoverLoses()
        {
            // A の列 0,1,2 のうち 2 に B の中がかぶっている。B が中を 6 に動かすと A の列が現れる
            var s = Board(1, At(0, A(S)), At(1, A(M)), At(2, A(S), B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Move(2, M, 6));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
            Assert.AreEqual(EndReason.RevealedLine, next.EndReason);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, next.WinningLine);
        }

        [Test]
        public void RevealedOpponentLine_CoveredByMovedPiece_NoLoss()
        {
            // 2 の大を持ち上げると A の列 0,1,2 が現れるが、同じ列の 0 にかぶせ直せば負けない
            var s = Board(1, At(0, A(S)), At(1, A(M)), At(2, A(S), B(L)));
            var next = _rules.ApplyMove(s, GobbletMove.Move(2, L, 0));
            Assert.IsFalse(next.Result.IsOver);
        }

        [Test]
        public void RevealedOpponentLine_TakesPriorityOverOwnLine()
        {
            // B が 2 の大を 5 に動かすと、B の列 3,4,5 と A の列 0,1,2 が同時にできる → A の勝ち
            var s = Board(1, At(0, A(S)), At(1, A(M)), At(2, A(S), B(L)), At(3, B(S)), At(4, B(M)));
            var next = _rules.ApplyMove(s, GobbletMove.Move(2, L, 5));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
            Assert.AreEqual(EndReason.RevealedLine, next.EndReason);
        }

        [Test]
        public void TurnLimit_IsDraw()
        {
            var s = GobbletState.FromBoard(new IReadOnlyList<Piece>[9], 0, GobbletState.MaxPlies - 1);
            var next = _rules.ApplyMove(s, GobbletMove.Place(S, 4));
            Assert.AreEqual(GameStatus.Draw, next.Result.Status);
            Assert.AreEqual(EndReason.TurnLimit, next.EndReason);
        }

        [Test]
        public void LineOnLastPly_WinsInsteadOfDraw()
        {
            var stacks = new IReadOnlyList<Piece>[9];
            stacks[0] = new[] { A(S) };
            stacks[1] = new[] { A(M) };
            var s = GobbletState.FromBoard(stacks, 0, GobbletState.MaxPlies - 1);
            var next = _rules.ApplyMove(s, GobbletMove.Place(L, 2));
            Assert.AreEqual(GameResult.WinFor(0), next.Result);
        }

        [Test]
        public void RandomPlayouts_AlwaysEndAndKeepPieceCounts()
        {
            var rng = new Random(7);
            for (int game = 0; game < 200; game++)
            {
                var s = GobbletState.Initial(game % 2);
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.Greater(moves.Count, 0);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);

                    for (int p = 0; p < 2; p++)
                    foreach (PieceSize z in Enum.GetValues(typeof(PieceSize)))
                    {
                        int onBoard = Enumerable.Range(0, 9).Sum(c => s.GetStack(c).Count(x => x.Owner == p && x.Size == z));
                        Assert.AreEqual(2, onBoard + s.HandCount(p, z));
                    }
                }
                Assert.LessOrEqual(s.Ply, GobbletState.MaxPlies);
            }
        }
    }
}
