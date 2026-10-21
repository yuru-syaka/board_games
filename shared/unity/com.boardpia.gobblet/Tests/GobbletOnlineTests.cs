using System;
using System.Linq;
using BoardGames.Core;
using BoardGames.Gobblet.Online;
using NUnit.Framework;

namespace BoardGames.Gobblet.Tests
{
    public class GobbletOnlineTests
    {
        LoopbackTransport _hostLink;
        LoopbackTransport _guestLink;
        OnlineMatch _host;
        OnlineMatch _guest;
        readonly GobbletRules _rules = new GobbletRules();

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
            var state = GobbletState.Initial(1);
            var rng = new Random(3);
            for (int i = 0; i < 7 && !state.Result.IsOver; i++)
            {
                var moves = _rules.GetLegalMoves(state);
                state = _rules.ApplyMove(state, moves[rng.Next(moves.Count)]);
            }

            Assert.IsTrue(GobbletNetProtocol.TryDecodeSnapshot(GobbletNetProtocol.EncodeSnapshot(state), out var decoded));
            AssertSameState(state, decoded);
        }

        [Test]
        public void Snapshot_RoundTripsFinishedGameWithWinningLine()
        {
            var state = GobbletState.Initial(0);
            state = _rules.ApplyMove(state, GobbletMove.Place(PieceSize.Large, 0));
            state = _rules.ApplyMove(state, GobbletMove.Place(PieceSize.Large, 3));
            state = _rules.ApplyMove(state, GobbletMove.Place(PieceSize.Large, 1));
            state = _rules.ApplyMove(state, GobbletMove.Place(PieceSize.Large, 4));
            state = _rules.ApplyMove(state, GobbletMove.Place(PieceSize.Medium, 2));
            Assert.IsTrue(state.Result.IsOver);

            Assert.IsTrue(GobbletNetProtocol.TryDecodeSnapshot(GobbletNetProtocol.EncodeSnapshot(state), out var decoded));
            AssertSameState(state, decoded);
            CollectionAssert.AreEqual(state.WinningLine.ToArray(), decoded.WinningLine.ToArray());
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[] { GobbletMove.Place(PieceSize.Small, 8), GobbletMove.Move(4, PieceSize.Large, 0) })
            {
                Assert.IsTrue(GobbletNetProtocol.TryDecodeMove(GobbletNetProtocol.EncodeMove(m), out var decoded));
                Assert.AreEqual(m, decoded);
            }
        }

        [Test]
        public void MalformedMessages_AreRejected()
        {
            foreach (var bad in new[]
                     {
                         "", "S", "S|9|x", "M|1|1,2", "M|1|a,b,c", "M|1|-2,0,0", "M|1|0,3,1", "M|1|0,0,9", "X|1",
                         "S|1|0|0|0|-1|0|-|-|", // マス数が足りない
                         "S|1|0|0|0|-1|0|-|-|0201;;;;;;;;", // 大の上に小
                         "S|1|0|0|0|-1|0|-|-|00;00;00;;;;;;", // 小が 3 つ
                         "S|1|0|0|0|-1|0|-|0,1,5|;;;;;;;;", // 存在しない列
                     })
            {
                Assert.IsFalse(GobbletNetProtocol.TryDecodeSnapshot(bad, out _), bad);
                Assert.IsFalse(GobbletNetProtocol.TryDecodeMove(bad, out _), bad);
            }
        }

        // ---------- 対局の進行 ----------

        [Test]
        public void StartNewGame_GivesBothSidesTheSameState()
        {
            _host.StartNewGame();
            Pump();
            Assert.IsNotNull(_guest.State);
            Assert.AreEqual(0, _guest.State.Ply);
            Assert.AreEqual(_host.State.CurrentPlayer, _guest.State.CurrentPlayer);
            Assert.AreEqual(0, _host.LocalPlayer);
            Assert.AreEqual(1, _guest.LocalPlayer);
        }

        [Test]
        public void FirstPlayer_IsRandomAcrossGames()
        {
            var seen = new bool[2];
            for (int i = 0; i < 20; i++)
            {
                _host.StartNewGame();
                seen[_host.State.CurrentPlayer] = true;
            }
            Assert.IsTrue(seen[0] && seen[1]);
        }

        [Test]
        public void FullGame_BothSidesAgreeOnEveryState()
        {
            _host.StartNewGame();
            Pump();
            var rng = new Random(8);
            int guard = 0;
            while (!_host.State.Result.IsOver && guard++ < 200)
            {
                var side = _host.State.CurrentPlayer == 0 ? _host : _guest;
                var moves = _rules.GetLegalMoves(side.State);
                Assert.IsTrue(side.SubmitMove(moves[rng.Next(moves.Count)]));
                Pump();
                AssertSameState(_host.State, _guest.State);
            }
            Assert.IsTrue(_guest.State.Result.IsOver);
        }

        [Test]
        public void MoveOutOfTurn_IsRefusedLocally()
        {
            _host.StartNewGame();
            Pump();
            var waiting = _host.State.CurrentPlayer == 0 ? _guest : _host;
            Assert.IsFalse(waiting.SubmitMove(GobbletMove.Place(PieceSize.Small, 0)));
            Pump();
            Assert.AreEqual(0, _host.State.Ply);
            Assert.AreEqual(0, _guest.State.Ply);
        }

        [Test]
        public void Host_RejectsIllegalMoveFromTamperedGuestAndResyncs()
        {
            _host.StartNewGame();
            Pump();
            if (_host.State.CurrentPlayer == 0)
            {
                _host.SubmitMove(GobbletMove.Place(PieceSize.Large, 4));
                Pump();
            }
            int ply = _host.State.Ply;
            string error = null;
            _host.ProtocolError += e => error = e;
            int guestUpdates = 0;
            _guest.StateChanged += _ => guestUpdates++;

            // 改造クライアントを想定: 盤に無い駒を動かす / 同じ大きさにかぶせる
            _guestLink.Send(GobbletNetProtocol.EncodeMove(GobbletMove.Move(0, PieceSize.Small, 1)));
            var top = _guest.State.Top(4);
            var illegal = top.HasValue
                ? GobbletMove.Place(top.Value.Size, 4)
                : GobbletMove.Move(8, PieceSize.Large, 4);
            _guestLink.Send(GobbletNetProtocol.EncodeMove(illegal));
            Pump();

            Assert.AreEqual(ply, _host.State.Ply);
            Assert.IsNotNull(error);
            Assert.AreEqual(2, guestUpdates, "host should resend the current state after each rejected move");
            Assert.AreEqual(ply, _guest.State.Ply);
        }

        [Test]
        public void Host_IgnoresSnapshotFromGuest()
        {
            _host.StartNewGame();
            Pump();
            string error = null;
            _host.ProtocolError += e => error = e;
            _guestLink.Send(GobbletNetProtocol.EncodeSnapshot(GobbletState.Initial(1)));
            Pump();
            Assert.IsNotNull(error);
            Assert.AreEqual(0, _host.State.Ply);
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
            Assert.IsTrue(_host.State.Result.IsOver, "must not restart with only one request");

            _host.RequestRematch();
            Pump();
            Assert.IsFalse(_host.State.Result.IsOver);
            Assert.AreEqual(0, _guest.State.Ply);
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

        // ---------- 補助 ----------

        void PlayRandomGame(Random rng)
        {
            int guard = 0;
            while (!_host.State.Result.IsOver && guard++ < 200)
            {
                var side = _host.State.CurrentPlayer == 0 ? _host : _guest;
                var moves = _rules.GetLegalMoves(side.State);
                side.SubmitMove(moves[rng.Next(moves.Count)]);
                Pump();
            }
            Assert.IsTrue(_host.State.Result.IsOver);
        }

        static void AssertSameState(GobbletState expected, GobbletState actual)
        {
            Assert.AreEqual(expected.CurrentPlayer, actual.CurrentPlayer);
            Assert.AreEqual(expected.Ply, actual.Ply);
            Assert.AreEqual(expected.Result.Status, actual.Result.Status);
            Assert.AreEqual(expected.Result.Winner, actual.Result.Winner);
            Assert.AreEqual(expected.EndReason, actual.EndReason);
            Assert.AreEqual(expected.LastMove, actual.LastMove);
            Assert.AreEqual(expected.WinningLine == null, actual.WinningLine == null);
            for (int c = 0; c < GobbletState.CellCount; c++)
                CollectionAssert.AreEqual(expected.GetStack(c).ToArray(), actual.GetStack(c).ToArray(), $"cell {c}");
            for (int p = 0; p < 2; p++)
            for (int z = 0; z < 3; z++)
                Assert.AreEqual(expected.HandCount(p, (PieceSize)z), actual.HandCount(p, (PieceSize)z));
        }
    }
}
