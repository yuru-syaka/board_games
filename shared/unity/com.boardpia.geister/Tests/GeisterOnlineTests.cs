using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;
using BoardGames.Geister.Online;
using NUnit.Framework;

namespace BoardGames.Geister.Tests
{
    public class GeisterOnlineTests
    {
        static readonly GhostType G = GhostType.Good;
        static readonly GhostType E = GhostType.Evil;

        LoopbackTransport _hostLink;
        LoopbackTransport _guestLink;
        OnlineMatch _host;
        OnlineMatch _guest;

        [SetUp]
        public void SetUp()
        {
            LoopbackTransport.CreatePair(out _hostLink, out _guestLink);
            _host = new OnlineMatch(_hostLink, true, new Random(1));
            _guest = new OnlineMatch(_guestLink, false);
        }

        void Pump() => _hostLink.Pump();

        // ---------- プロトコル ----------

        [Test]
        public void Snapshot_RoundTrips()
        {
            var state = GeisterSetup.Create(new[] { G, E, G, E, G, E, G, E }, new[] { E, E, G, G, E, E, G, G }, 1);
            var rules = new GeisterRules();
            state = rules.ApplyMove(state, rules.GetLegalMoves(state)[0]);
            var original = MatchSnapshot.For(state, 1);

            Assert.IsTrue(GeisterNetProtocol.TryDecodeSnapshot(GeisterNetProtocol.EncodeSnapshot(original), out var decoded));
            Assert.AreEqual(original.Viewer, decoded.Viewer);
            Assert.AreEqual(original.CurrentPlayer, decoded.CurrentPlayer);
            Assert.AreEqual(original.Ply, decoded.Ply);
            Assert.AreEqual(original.LastMove, decoded.LastMove);
            Assert.AreEqual(original.Result.Status, decoded.Result.Status);
            CollectionAssert.AreEqual(original.View.Ghosts.ToArray(), decoded.View.Ghosts.ToArray());
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[] { GeisterMove.Step(3, 1, 2, 1, 3), GeisterMove.Escape(9, 5, 0) })
            {
                Assert.IsTrue(GeisterNetProtocol.TryDecodeMove(GeisterNetProtocol.EncodeMove(m), out var decoded));
                Assert.AreEqual(m, decoded);
            }
        }

        [Test]
        public void MalformedMessages_AreRejected()
        {
            foreach (var bad in new[] { "", "S", "S|9|x", "M|1|1,2", "M|1|a,b,c,d,e,f", "X|1", "S|1|0|0|0|0|-1|0|-1|-|" })
            {
                Assert.IsFalse(GeisterNetProtocol.TryDecodeSnapshot(bad, out _), bad);
                Assert.IsFalse(GeisterNetProtocol.TryDecodeMove(bad, out _), bad);
            }
        }

        // ---------- 対局の進行 ----------

        [Test]
        public void StartNewGame_GivesBothSidesTheSameBoard()
        {
            _host.StartNewGame();
            Pump();
            Assert.IsNotNull(_guest.Snapshot);
            Assert.AreEqual(1, _guest.Snapshot.Viewer);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.AreEqual(_host.Snapshot.CurrentPlayer, _guest.Snapshot.CurrentPlayer);
            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(_host.Snapshot.View.Ghosts[i].X, _guest.Snapshot.View.Ghosts[i].X);
                Assert.AreEqual(_host.Snapshot.View.Ghosts[i].Y, _guest.Snapshot.View.Ghosts[i].Y);
            }
        }

        [Test]
        public void Guest_NeverReceivesHostTypesUntilCapturedOrGameOver()
        {
            var rng = new Random(5);
            for (int game = 0; game < 5; game++)
            {
                _hostLink.SentLog.Clear();
                _host.StartNewGame();
                Pump();
                PlayRandomGame(rng);

                foreach (var msg in _hostLink.SentLog)
                {
                    if (GeisterNetProtocol.KindOf(msg) != NetMessageKind.Snapshot) continue;
                    Assert.IsTrue(GeisterNetProtocol.TryDecodeSnapshot(msg, out var s));
                    if (s.Result.IsOver) continue;
                    foreach (var g in s.View.Ghosts)
                    {
                        if (g.Owner == 0 && g.IsOnBoard)
                            Assert.AreEqual(GhostType.Unknown, g.Type, "host ghost leaked to guest");
                        if (g.Owner == 0 && g.Status == GhostStatus.Captured)
                            Assert.AreNotEqual(GhostType.Unknown, g.Type, "captured ghost should be revealed");
                    }
                }

                // 決着後は全公開
                Assert.IsTrue(_guest.Snapshot.Result.IsOver);
                Assert.IsTrue(_guest.Snapshot.View.Ghosts.All(g => g.Type != GhostType.Unknown));
            }
        }

        [Test]
        public void FullGame_BothSidesAgreeOnResult()
        {
            _host.StartNewGame();
            Pump();
            PlayRandomGame(new Random(8));
            Assert.IsTrue(_host.Snapshot.Result.IsOver);
            Assert.AreEqual(_host.Snapshot.Result.Status, _guest.Snapshot.Result.Status);
            Assert.AreEqual(_host.Snapshot.Result.Winner, _guest.Snapshot.Result.Winner);
            Assert.AreEqual(_host.Snapshot.EndReason, _guest.Snapshot.EndReason);
            Assert.AreEqual(_host.Snapshot.Ply, _guest.Snapshot.Ply);
        }

        [Test]
        public void GuestMoveOutOfTurn_IsIgnored()
        {
            _host.StartNewGame();
            Pump();
            if (_host.Snapshot.CurrentPlayer != 0)
            {
                // 先手がゲストなら 1 手進めてホストの手番にする
                _guest.SubmitLocalMove(_guest.Snapshot.View.GetLegalMoves()[0]);
                Pump();
            }
            int ply = _host.Snapshot.Ply;
            // ゲストの視点ではホストの手番なので送られない
            var anyGuestGhost = _guest.Snapshot.View.Ghosts.First(g => g.Owner == 1 && g.IsOnBoard);
            _guest.SubmitLocalMove(GeisterMove.Step(anyGuestGhost.Id, anyGuestGhost.X, anyGuestGhost.Y, anyGuestGhost.X, anyGuestGhost.Y - 1));
            Pump();
            Assert.AreEqual(ply, _host.Snapshot.Ply);
        }

        [Test]
        public void Host_RejectsIllegalMoveFromTamperedGuestAndResyncs()
        {
            _host.StartNewGame();
            Pump();
            if (_host.Snapshot.CurrentPlayer == 0)
            {
                _host.SubmitLocalMove(_host.Snapshot.View.GetLegalMoves()[0]);
                Pump();
            }
            int ply = _host.Snapshot.Ply;
            string error = null;
            _host.ProtocolError += e => error = e;
            int guestUpdates = 0;
            _guest.SnapshotChanged += _ => guestUpdates++;

            // 改造クライアントを想定: ホストの駒を動かそうとする / 2 マス動かそうとする
            _guestLink.Send(GeisterNetProtocol.EncodeMove(GeisterMove.Step(0, 1, 0, 0, 0)));
            var mine = _guest.Snapshot.View.Ghosts.First(g => g.Owner == 1 && g.IsOnBoard);
            _guestLink.Send(GeisterNetProtocol.EncodeMove(GeisterMove.Step(mine.Id, mine.X, mine.Y, mine.X, mine.Y - 2)));
            Pump();

            Assert.AreEqual(ply, _host.Snapshot.Ply);
            Assert.IsNotNull(error);
            Assert.AreEqual(2, guestUpdates, "host should resend the current snapshot after each rejected move");
            Assert.AreEqual(ply, _guest.Snapshot.Ply);
        }

        [Test]
        public void Rematch_StartsOnlyWhenBothRequest()
        {
            _host.StartNewGame();
            Pump();
            PlayRandomGame(new Random(3));

            _guest.RequestRematch();
            Pump();
            Assert.IsTrue(_host.OpponentWantsRematch);
            Assert.IsTrue(_host.Snapshot.Result.IsOver, "must not restart with only one request");

            _host.RequestRematch();
            Pump();
            Assert.IsFalse(_host.Snapshot.Result.IsOver);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.IsFalse(_guest.LocalWantsRematch);
            Assert.IsFalse(_host.OpponentWantsRematch);
        }

        [Test]
        public void RematchRequest_DuringGame_IsIgnored()
        {
            _host.StartNewGame();
            Pump();
            _guest.RequestRematch();
            Pump();
            Assert.IsFalse(_host.OpponentWantsRematch);
            Assert.IsFalse(_guest.LocalWantsRematch);
        }

        [Test]
        public void GuestRejectsSnapshotForWrongViewer()
        {
            _host.StartNewGame();
            Pump();
            string error = null;
            _guest.ProtocolError += e => error = e;
            // ホスト視点(P0)の局面を誤ってゲストに送った場合は受け付けない
            _hostLink.Send(GeisterNetProtocol.EncodeSnapshot(_host.Snapshot));
            Pump();
            Assert.IsNotNull(error);
            Assert.AreEqual(1, _guest.Snapshot.Viewer);
        }

        // ---------- CPU 戦 ----------

        [Test]
        public void LocalCpuMatch_PlaysToTheEnd()
        {
            var match = new LocalCpuMatch(new Random(4));
            var rng = new Random(4);
            match.StartNewGame();
            int guard = 0;
            while (!match.Snapshot.Result.IsOver && guard++ < 1000)
            {
                if (match.IsCpuTurn) match.PlayCpuTurn();
                else
                {
                    var moves = match.Snapshot.View.GetLegalMoves();
                    match.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                }
            }
            Assert.IsTrue(match.Snapshot.Result.IsOver);
            Assert.IsTrue(match.Snapshot.View.Ghosts.All(g => g.Type != GhostType.Unknown));
        }

        [Test]
        public void LocalCpuMatch_HidesCpuTypesDuringGame()
        {
            var match = new LocalCpuMatch(new Random(2));
            match.StartNewGame();
            Assert.IsTrue(match.Snapshot.View.Ghosts.Where(g => g.Owner == 1).All(g => g.Type == GhostType.Unknown));
        }

        // ---------- 補助 ----------

        void PlayRandomGame(Random rng)
        {
            int guard = 0;
            while (!_host.Snapshot.Result.IsOver && guard++ < 1000)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                var moves = side.Snapshot.View.GetLegalMoves();
                Assert.IsNotEmpty(moves);
                side.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                Pump();
            }
            Assert.IsTrue(_host.Snapshot.Result.IsOver);
        }
    }
}
