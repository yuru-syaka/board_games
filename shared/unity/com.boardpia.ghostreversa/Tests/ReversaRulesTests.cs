using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostReversa.Tests
{
    public class ReversaRulesTests
    {
        readonly ReversaRules _rules = new ReversaRules();

        // ---------- 補助 ----------

        static Tile? Up(int owner, TileKind kind) => new Tile(owner, kind, true);
        static Tile? Down(int owner, TileKind kind) => new Tile(owner, kind, false);

        /// <summary>
        /// 盤を上の行(y = 2)から下の行(y = 0)の順に 9 マスで書いて対局中の局面を作る。
        /// 手駒は両者とも青 2・赤 2、捨てた色は両者とも赤(勝てる色は青)。
        /// </summary>
        static ReversaState Board(int current, params Tile?[] rowsTopDown)
        {
            var cells = new Tile?[9];
            for (int row = 0; row < 3; row++)
            for (int x = 0; x < 3; x++)
                cells[ReversaBoard.Index(x, 2 - row)] = rowsTopDown[row * 3 + x];
            return ReversaSetup.CreateCustom(cells, new[] { 2, 2 }, new[] { 2, 2 }, new[] { TileKind.Red, TileKind.Red }, 0, current, 10);
        }

        static ReversaState WithHands(ReversaState s, int b0, int r0, int b1, int r1) =>
            ReversaSetup.CreateCustom(s.Cells, new[] { b0, b1 }, new[] { r0, r1 }, new[] { s.DiscardOf(0), s.DiscardOf(1) },
                s.FirstPlayer, s.CurrentPlayer, s.Ply);

        static ReversaState Empty(int current) => Board(current, null, null, null, null, null, null, null, null, null);

        // ---------- 準備(捨てる手) ----------

        [Test]
        public void NewGame_StartsWithDiscardByFirstPlayer()
        {
            var s = ReversaSetup.Create(1);
            Assert.AreEqual(GamePhase.Discard, s.Phase);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(6, s.HandCount(0));
            Assert.AreEqual(6, s.HandCount(1));
            CollectionAssert.AreEquivalent(new[] { ReversaMove.Discard(TileKind.Blue), ReversaMove.Discard(TileKind.Red) },
                _rules.GetLegalMoves(s));
            Assert.IsTrue(s.Cells.All(c => !c.HasValue));
        }

        [Test]
        public void Discards_FirstThenSecond_ThenFirstPlayerStarts()
        {
            var s = ReversaSetup.Create(1);
            s = _rules.ApplyMove(s, ReversaMove.Discard(TileKind.Red));
            Assert.AreEqual(GamePhase.Discard, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.AreEqual(TileKind.Red, s.DiscardOf(1));
            Assert.AreEqual(2, s.HandOf(1, TileKind.Red));

            s = _rules.ApplyMove(s, ReversaMove.Discard(TileKind.Blue));
            Assert.AreEqual(GamePhase.Play, s.Phase);
            Assert.AreEqual(1, s.CurrentPlayer, "先手から指す");
            Assert.AreEqual(2, s.HandOf(0, TileKind.Blue));
            Assert.AreEqual(5, s.HandCount(0));
            Assert.AreEqual(2, s.Ply);
        }

        [Test]
        public void PlaceMove_IsIllegalDuringDiscard()
        {
            var s = ReversaSetup.Create(0);
            Assert.IsFalse(_rules.IsLegal(s, ReversaMove.Place(TileKind.Blue, 0, 0, true)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 0, 0, true)));
        }

        // ---------- 置く手と裏返し ----------

        [Test]
        public void FaceDownPlacement_FlipsNothing()
        {
            var s = Board(0,
                Up(1, TileKind.Blue), null, null,
                null, null, null,
                Up(1, TileKind.Red), null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 1, 0, false));
            Assert.AreEqual(Down(0, TileKind.Blue), next.At(1, 0));
            Assert.AreEqual(Up(1, TileKind.Red), next.At(0, 0));
            Assert.AreEqual(Up(1, TileKind.Blue), next.At(0, 2));
            Assert.AreEqual(1, next.HandOf(0, TileKind.Blue));
            Assert.AreEqual(1, next.CurrentPlayer);
        }

        [Test]
        public void FaceUpBlue_FlipsTheWholeRowOnly()
        {
            var s = Board(0,
                Up(1, TileKind.Red), null, null,
                Up(1, TileKind.Blue), null, Down(0, TileKind.Red),
                null, null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 1, 1, true));
            Assert.AreEqual(Down(1, TileKind.Blue), next.At(0, 1), "同じ行: 表 → 裏");
            Assert.AreEqual(Up(0, TileKind.Red), next.At(2, 1), "同じ行: 裏 → 表");
            Assert.AreEqual(Up(0, TileKind.Blue), next.At(1, 1), "置いた駒は表のまま");
            Assert.AreEqual(Up(1, TileKind.Red), next.At(0, 2), "他の行は変わらない");
        }

        [Test]
        public void FaceUpRed_FlipsTheWholeColumnOnly()
        {
            var s = Board(0,
                null, Up(1, TileKind.Blue), null,
                Down(1, TileKind.Red), null, null,
                null, Down(0, TileKind.Blue), null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Red, 1, 1, true));
            Assert.AreEqual(Down(1, TileKind.Blue), next.At(1, 2));
            Assert.AreEqual(Up(0, TileKind.Blue), next.At(1, 0));
            Assert.AreEqual(Down(1, TileKind.Red), next.At(0, 1), "他の列は変わらない");
        }

        [Test]
        public void PlacingOnOpponentFaceDownTile_ReturnsItToTheirHand()
        {
            var s = Board(0,
                null, null, null,
                null, Down(1, TileKind.Red), null,
                null, null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 1, 1, false));
            Assert.AreEqual(Down(0, TileKind.Blue), next.At(1, 1));
            Assert.AreEqual(3, next.HandOf(1, TileKind.Red));
            Assert.AreEqual(1, next.HandOf(0, TileKind.Blue));
        }

        [Test]
        public void CannotPlaceOnOwnTile_FaceUpTile_OrPoltergeist()
        {
            var s = Board(0,
                Down(0, TileKind.Blue), Up(1, TileKind.Red), Tile.Poltergeist,
                null, null, null,
                null, null, null);
            var moves = _rules.GetLegalMoves(s);
            Assert.IsFalse(moves.Any(m => m.Kind == MoveKind.Place && m.Y == 2), "上の行には置けない");
        }

        [Test]
        public void PieceNotInHand_CannotBePlaced()
        {
            var s = WithHands(Empty(0), 0, 2, 2, 2);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Piece == TileKind.Blue));
            Assert.IsTrue(_rules.GetLegalMoves(s).Any(m => m.Piece == TileKind.Red));
        }

        // ---------- ポルターガイスト ----------

        [Test]
        public void Poltergeist_FlipsEverythingElse()
        {
            var s = Board(0,
                Up(1, TileKind.Blue), Down(0, TileKind.Red), null,
                null, null, null,
                null, null, Down(1, TileKind.Blue));
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Poltergeist, 1, 1, true));
            Assert.AreEqual(Down(1, TileKind.Blue), next.At(0, 2));
            Assert.AreEqual(Up(0, TileKind.Red), next.At(1, 2));
            Assert.AreEqual(Up(1, TileKind.Blue), next.At(2, 0));
            Assert.AreEqual(Tile.Poltergeist, next.At(1, 1));
            Assert.AreEqual(4, next.HandCount(0), "手駒は減らない");
        }

        [Test]
        public void Poltergeist_OnlyOnEmptyCells_AndOnlyFaceUp()
        {
            var s = Board(0,
                Down(1, TileKind.Blue), null, null,
                null, null, null,
                null, null, null);
            var polter = _rules.GetLegalMoves(s).Where(m => m.Piece == TileKind.Poltergeist).ToList();
            Assert.AreEqual(8, polter.Count);
            Assert.IsTrue(polter.All(m => m.FaceUp));
            Assert.AreEqual(ReversaMove.Place(TileKind.Poltergeist, 0, 0, true), ReversaMove.Place(TileKind.Poltergeist, 0, 0, false),
                "裏で置こうとしても表になる");
        }

        [Test]
        public void Poltergeist_CannotBePlacedWhileOnBoard()
        {
            var s = Board(0,
                Tile.Poltergeist, null, null,
                null, null, null,
                null, null, null);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Piece == TileKind.Poltergeist));
        }

        [Test]
        public void FlippedPoltergeist_LeavesTheBoard_AndCanBePlacedAgain()
        {
            var s = Board(0,
                null, null, null,
                Tile.Poltergeist, null, null,
                null, null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 1, true));
            Assert.IsFalse(next.At(0, 1).HasValue);
            Assert.IsFalse(next.PoltergeistOnBoard);
            Assert.IsTrue(_rules.GetLegalMoves(next).Any(m => m.Piece == TileKind.Poltergeist));
        }

        [Test]
        public void FaceUpPoltergeist_DoesNotCountForAnyone()
        {
            var s = Board(0,
                Up(0, TileKind.Blue), Up(0, TileKind.Blue), null,
                null, null, null,
                null, null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Poltergeist, 2, 0, true));
            Assert.IsFalse(next.Result.IsOver);
        }

        // ---------- 勝敗 ----------

        [Test]
        public void ThreeOfOwnColorFaceUp_Wins_EvenIfNotInALine()
        {
            var s = Board(0,
                Up(0, TileKind.Blue), null, null,
                null, null, null,
                null, null, Up(0, TileKind.Blue));
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 1, 1, false));
            Assert.IsFalse(next.Result.IsOver, "裏で置いた駒は数えない");

            next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Red, 1, 1, true));
            Assert.IsFalse(next.Result.IsOver, "色が混ざっていたら勝ちではない");

            var win = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 1, true));
            Assert.AreEqual(GameResult.WinFor(0).ToString(), win.Result.ToString());
            Assert.AreEqual(EndReason.Completed, win.EndReason);
        }

        [Test]
        public void FlippingOpponentsThirdPieceFaceUp_GivesThemTheWin()
        {
            var s = Board(0,
                Up(1, TileKind.Red), null, null,
                Up(1, TileKind.Red), null, Down(1, TileKind.Red),
                null, null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 1, 1, true));
            // 行 y=1: (0,1) は表 → 裏、(2,1) は裏 → 表。相手の赤の表は (0,2) と (2,1) の 2 体で、まだ勝ちではない
            Assert.IsFalse(next.Result.IsOver);

            var s2 = Board(0,
                Up(1, TileKind.Red), null, null,
                Up(1, TileKind.Red), null, null,
                null, null, Down(1, TileKind.Red));
            var gift = _rules.ApplyMove(s2, ReversaMove.Place(TileKind.Blue, 1, 0, true));
            Assert.AreEqual(1, gift.Result.Winner);
            Assert.AreEqual(EndReason.Gifted, gift.EndReason);
        }

        [Test]
        public void BothSidesCompleteAtOnce_MoverWins()
        {
            var s = Board(0,
                Up(0, TileKind.Blue), Up(1, TileKind.Red), null,
                null, Up(1, TileKind.Red), null,
                Down(1, TileKind.Red), Down(0, TileKind.Blue), null);
            // 行 y=0 が裏返り、P0 の青 3 体・P1 の赤 3 体が同時にそろう
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 0, true));
            Assert.AreEqual(3, next.FaceUpCount(0, TileKind.Blue));
            Assert.AreEqual(3, next.FaceUpCount(1, TileKind.Red));
            Assert.AreEqual(0, next.Result.Winner);
            Assert.AreEqual(EndReason.Completed, next.EndReason);
        }

        [Test]
        public void PoltergeistOnThreeFaceDownOwnTiles_WinsInstantly()
        {
            var s = Board(0,
                Down(0, TileKind.Blue), null, Down(0, TileKind.Blue),
                null, null, null,
                Down(0, TileKind.Blue), null, null);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Poltergeist, 1, 1, true));
            Assert.AreEqual(0, next.Result.Winner);
        }

        // ---------- パス・引き分け ----------

        [Test]
        public void PlayerWithoutPlacements_MustPass()
        {
            // P1 の手駒が無く、ポルターガイストは盤上 → P1 はパスだけ
            var s = WithHands(Board(0,
                Tile.Poltergeist, null, null,
                null, null, null,
                null, null, null), 2, 2, 0, 0);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 2, false));
            Assert.IsFalse(next.Result.IsOver);
            Assert.AreEqual(1, next.CurrentPlayer);
            CollectionAssert.AreEqual(new[] { ReversaMove.Pass() }, _rules.GetLegalMoves(next));

            var afterPass = _rules.ApplyMove(next, ReversaMove.Pass());
            Assert.AreEqual(0, afterPass.CurrentPlayer);
            Assert.IsTrue(_rules.GetLegalMoves(afterPass).All(m => m.Kind == MoveKind.Place));
        }

        [Test]
        public void Pass_IsIllegalWhenAPlacementExists()
        {
            Assert.IsFalse(_rules.IsLegal(Empty(0), ReversaMove.Pass()));
        }

        [Test]
        public void NobodyCanPlace_IsADraw()
        {
            var s = WithHands(Board(0,
                Tile.Poltergeist, null, null,
                null, null, null,
                null, null, null), 1, 0, 0, 0);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 2, false));
            Assert.AreEqual(GameStatus.Draw, next.Result.Status);
            Assert.AreEqual(EndReason.NoMoves, next.EndReason);
        }

        [Test]
        public void TurnLimit_IsADraw()
        {
            var s = ReversaSetup.CreateCustom(new Tile?[9], new[] { 2, 2 }, new[] { 2, 2 }, new[] { TileKind.Red, TileKind.Red }, 0, 0,
                ReversaBoard.MaxPlies - 1);
            var next = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 0, 0, false));
            Assert.AreEqual(GameStatus.Draw, next.Result.Status);
            Assert.AreEqual(EndReason.TurnLimit, next.EndReason);
        }

        [Test]
        public void NoMovesAfterGameOver()
        {
            var s = Board(0,
                Up(0, TileKind.Blue), Up(0, TileKind.Blue), null,
                null, null, null,
                null, null, null);
            var won = _rules.ApplyMove(s, ReversaMove.Place(TileKind.Blue, 2, 0, true));
            Assert.IsTrue(won.Result.IsOver);
            Assert.AreEqual(0, _rules.GetLegalMoves(won).Count);
        }

        // ---------- 全体 ----------

        [Test]
        public void RandomGames_AlwaysEnd_AndKeepPieceCounts()
        {
            var rng = new Random(7);
            for (int game = 0; game < 300; game++)
            {
                var s = ReversaSetup.CreateRandom(rng);
                int guard = 0;
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.IsNotEmpty(moves);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                    Assert.Less(++guard, ReversaBoard.MaxPlies + 1);
                    if (s.Phase != GamePhase.Play) continue;
                    for (int p = 0; p < 2; p++)
                    {
                        int onBoard = s.Cells.Count(c => c.HasValue && c.Value.Owner == p);
                        Assert.AreEqual(5, onBoard + s.HandCount(p), "捨てた 1 体以外の 5 体は盤上か手駒にある");
                        Assert.LessOrEqual(s.HandOf(p, s.DiscardOf(p)) +
                                           s.Cells.Count(c => c.HasValue && c.Value.Owner == p && c.Value.Kind == s.DiscardOf(p)), 2);
                    }
                    Assert.LessOrEqual(s.Cells.Count(c => c.HasValue && c.Value.IsPoltergeist), 1);
                    Assert.IsFalse(s.Cells.Any(c => c.HasValue && c.Value.IsPoltergeist && !c.Value.FaceUp));
                }
            }
        }
    }
}
