using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Geister.Tests
{
    public class GeisterRulesTests
    {
        static readonly GhostType G = GhostType.Good;
        static readonly GhostType E = GhostType.Evil;

        readonly GeisterRules _rules = new GeisterRules();

        static GhostType[] Layout() => new[] { G, G, G, G, E, E, E, E };

        static Ghost On(int id, int owner, GhostType type, int x, int y) =>
            new Ghost(id, owner, type, x, y, GhostStatus.OnBoard);

        static Ghost Captured(int id, int owner, GhostType type) =>
            new Ghost(id, owner, type, -1, -1, GhostStatus.Captured);

        // ---------- 初期配置 ----------

        [Test]
        public void Setup_PlacesEightGhostsPerPlayerInStartArea()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            Assert.AreEqual(16, s.Ghosts.Count);
            foreach (var g in s.Ghosts)
            {
                Assert.That(g.X, Is.InRange(1, 4));
                if (g.Owner == 0) Assert.That(g.Y, Is.InRange(0, 1));
                else Assert.That(g.Y, Is.InRange(4, 5));
            }
            Assert.AreEqual(4, s.Ghosts.Count(g => g.Owner == 0 && g.Type == G));
            Assert.AreEqual(4, s.Ghosts.Count(g => g.Owner == 1 && g.Type == E));
            Assert.AreEqual(GameStatus.Ongoing, _rules.GetResult(s).Status);
        }

        [Test]
        public void Setup_RejectsInvalidLayout()
        {
            Assert.Throws<ArgumentException>(() =>
                GeisterSetup.Create(new[] { G, G, G, G, G, E, E, E }, Layout(), 0));
            Assert.Throws<ArgumentException>(() => GeisterSetup.Create(new[] { G, E }, Layout(), 0));
        }

        [Test]
        public void RandomLayout_AlwaysHasFourOfEach()
        {
            var rng = new Random(1);
            for (int i = 0; i < 50; i++)
            {
                var l = GeisterSetup.RandomLayout(rng);
                Assert.AreEqual(4, l.Count(t => t == G));
                Assert.AreEqual(4, l.Count(t => t == E));
            }
        }

        // ---------- 合法手 ----------

        [Test]
        public void InitialPosition_HasExpectedMoveCount()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            // 手前の列 (y=1) の 4 体: 上へ 4 手 + 両端が横へ 2 手 = 6
            // 奥の列 (y=0) の 4 体: 下は盤外、上は味方 → 両端が横へ 2 手 = 2
            Assert.AreEqual(8, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void CannotMoveOntoOwnGhostOrOffBoard()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            var moves = _rules.GetLegalMoves(s);
            foreach (var m in moves)
            {
                Assert.IsTrue(GeisterBoard.InBounds(m.ToX, m.ToY));
                var occupant = s.GhostAt(m.ToX, m.ToY);
                Assert.IsTrue(occupant == null || occupant.Value.Owner != 0);
            }
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Step(0, 1, 0, 1, 1)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Step(0, 1, 0, 1, -1)));
        }

        [Test]
        public void CannotMoveDiagonallyOrTwoSquares()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Step(4, 1, 1, 2, 2)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Step(4, 1, 1, 1, 3)));
        }

        [Test]
        public void CannotMoveOpponentGhost()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Step(12, 1, 4, 1, 3)));
        }

        [Test]
        public void ApplyMove_SwitchesTurnAndDoesNotMutateOriginal()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(4, 1, 1, 1, 2));
            Assert.AreEqual(1, next.CurrentPlayer);
            Assert.AreEqual(1, next.Ply);
            Assert.AreEqual(2, next.GetGhost(4).Y);
            Assert.AreEqual(1, s.GetGhost(4).Y);
            Assert.AreEqual(0, s.CurrentPlayer);
        }

        [Test]
        public void MovingOntoOpponent_CapturesIt()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 0, 0), On(2, 0, G, 5, 1),
                On(8, 1, E, 2, 3), On(9, 1, G, 4, 4), On(10, 1, G, 3, 5),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            Assert.AreEqual(GhostStatus.Captured, next.GetGhost(8).Status);
            Assert.AreEqual(8, next.LastCapturedId);
            Assert.AreEqual(1, next.CapturedCount(1, E));
            Assert.AreEqual(0, next.GhostAt(2, 3).Value.Id);
            Assert.AreEqual(GameStatus.Ongoing, next.Result.Status);
        }

        // ---------- 脱出 ----------

        [Test]
        public void GoodGhostOnOpponentCorner_CanEscapeAndWins()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 0, 5), On(1, 0, E, 2, 0),
                On(8, 1, G, 3, 3), On(9, 1, E, 4, 4),
            }, 0);
            var moves = _rules.GetLegalMoves(s);
            var escape = moves.Single(m => m.IsEscape);
            Assert.AreEqual(0, escape.GhostId);

            var next = _rules.ApplyMove(s, escape);
            Assert.AreEqual(GameStatus.Win, next.Result.Status);
            Assert.AreEqual(0, next.Result.Winner);
            Assert.AreEqual(EndReason.Escaped, next.EndReason);
            Assert.AreEqual(GhostStatus.Escaped, next.GetGhost(0).Status);
        }

        [Test]
        public void EvilGhostOnOpponentCorner_CannotEscape()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, E, 5, 5), On(1, 0, G, 2, 0),
                On(8, 1, G, 3, 3), On(9, 1, E, 4, 2),
            }, 0);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.IsEscape));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GeisterMove.Escape(0, 5, 5)));
        }

        [Test]
        public void GoodGhostOnOwnCornerOrMiddle_CannotEscape()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 0, 0), On(1, 0, G, 2, 5),
                On(8, 1, G, 3, 3), On(9, 1, E, 4, 2),
            }, 0);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.IsEscape));
        }

        [Test]
        public void Player1_EscapesFromBottomCorners()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 3, 3),
                On(8, 1, G, 5, 0), On(9, 1, E, 0, 5),
            }, 1);
            var escape = _rules.GetLegalMoves(s).Single(m => m.IsEscape);
            Assert.AreEqual(8, escape.GhostId);
            var next = _rules.ApplyMove(s, escape);
            Assert.AreEqual(1, next.Result.Winner);
        }

        // ---------- 勝敗 ----------

        [Test]
        public void CapturingFourthGoodGhost_Wins()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, E, 2, 2), On(1, 0, G, 0, 0),
                Captured(8, 1, G), Captured(9, 1, G), Captured(10, 1, G),
                On(11, 1, G, 2, 3), On(12, 1, E, 4, 4),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            Assert.AreEqual(GameStatus.Win, next.Result.Status);
            Assert.AreEqual(0, next.Result.Winner);
            Assert.AreEqual(EndReason.CapturedAllGood, next.EndReason);
            Assert.AreEqual(next.Result.Winner, _rules.GetResult(next).Winner);
        }

        [Test]
        public void CapturingFourthEvilGhost_LosesForCapturer()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, G, 0, 0),
                Captured(8, 1, E), Captured(9, 1, E), Captured(10, 1, E),
                On(11, 1, E, 2, 3), On(12, 1, G, 4, 4),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            Assert.AreEqual(GameStatus.Win, next.Result.Status);
            Assert.AreEqual(1, next.Result.Winner);
            Assert.AreEqual(EndReason.LostAllEvil, next.EndReason);
        }

        [Test]
        public void CapturingThirdGoodGhost_DoesNotEndGame()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, E, 2, 2), On(1, 0, G, 0, 0),
                Captured(8, 1, G), Captured(9, 1, G),
                On(10, 1, G, 2, 3), On(11, 1, G, 4, 4), On(12, 1, E, 5, 5),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            Assert.AreEqual(GameStatus.Ongoing, next.Result.Status);
        }

        [Test]
        public void NoMovesAfterGameOver()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 0, 5), On(8, 1, G, 3, 3),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Escape(0, 0, 5));
            Assert.IsEmpty(_rules.GetLegalMoves(next));
        }

        [Test]
        public void ReachingPlyLimit_IsDraw()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 0, 0),
                On(8, 1, G, 4, 4), On(9, 1, E, 5, 3),
            }, 0, GeisterBoard.MaxPlies - 1);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            Assert.AreEqual(GameStatus.Draw, next.Result.Status);
            Assert.AreEqual(EndReason.TurnLimit, next.EndReason);
        }

        [Test]
        public void PlayerWithGhostsOnBoard_AlwaysHasMoves()
        {
            // 合法手なしは「全駒が自軍の駒と盤端に塞がれる」形でしか起きず、
            // それには盤の外周を埋める必要があるため 8 体では発生しない(勝敗 4 は保険)。
            var rng = new Random(7);
            for (int i = 0; i < 20; i++)
            {
                var s = GeisterSetup.CreateRandom(rng);
                Assert.IsNotEmpty(_rules.GetLegalMoves(s));
            }
        }

        // ---------- PlayerView ----------

        [Test]
        public void PlayerView_HidesOpponentTypesOnBoard()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            var view = GeisterPlayerView.Create(s, 0);
            foreach (var g in view.Ghosts)
            {
                if (g.Owner == 0) Assert.AreNotEqual(GhostType.Unknown, g.Type);
                else Assert.AreEqual(GhostType.Unknown, g.Type);
            }
        }

        [Test]
        public void PlayerView_RevealsCapturedGhosts()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 0, 0),
                On(8, 1, E, 2, 3), On(9, 1, G, 4, 4), On(10, 1, G, 5, 5),
            }, 0);
            var next = _rules.ApplyMove(s, GeisterMove.Step(0, 2, 2, 2, 3));
            var view = GeisterPlayerView.Create(next, 0);
            Assert.AreEqual(GhostType.Evil, view.Ghosts.Single(g => g.Id == 8).Type);
            Assert.AreEqual(GhostType.Unknown, view.Ghosts.Single(g => g.Id == 9).Type);
            Assert.AreEqual(1, view.CapturedCount(1, E));
        }

        [Test]
        public void PlayerView_LegalMovesMatchRules()
        {
            var rng = new Random(3);
            var s = GeisterSetup.CreateRandom(rng);
            for (int i = 0; i < 30 && !s.Result.IsOver; i++)
            {
                var view = GeisterPlayerView.Create(s, s.CurrentPlayer);
                CollectionAssert.AreEquivalent(_rules.GetLegalMoves(s), view.GetLegalMoves());
                var moves = _rules.GetLegalMoves(s);
                s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
            }
        }

        [Test]
        public void PlayerView_NotYourTurn_HasNoMoves()
        {
            var s = GeisterSetup.Create(Layout(), Layout(), 0);
            Assert.IsEmpty(GeisterPlayerView.Create(s, 1).GetLegalMoves());
        }

        // ---------- ランダム対局 ----------

        [Test]
        public void RandomGames_AlwaysTerminateWithValidResult()
        {
            var rng = new Random(42);
            for (int game = 0; game < 50; game++)
            {
                var s = GeisterSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    var moves = _rules.GetLegalMoves(s);
                    Assert.IsNotEmpty(moves);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
                Assert.LessOrEqual(s.Ply, GeisterBoard.MaxPlies);
                Assert.AreNotEqual(EndReason.None, s.EndReason);
            }
        }
    }
}
