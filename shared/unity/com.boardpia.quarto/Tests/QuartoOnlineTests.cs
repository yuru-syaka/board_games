using System;
using System.Linq;
using BoardGames.Core;
using BoardGames.Quarto.Online;
using NUnit.Framework;

namespace BoardGames.Quarto.Tests
{
    public class QuartoOnlineTests
    {
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
            var rules = new QuartoRules();
            var state = QuartoSetup.Create(1);
            state = rules.ApplyMove(state, QuartoMove.Give(5));
            state = rules.ApplyMove(state, QuartoMove.Place(2, 3));
            state = rules.ApplyMove(state, QuartoMove.Give(12));
            var original = MatchSnapshot.For(state, 1);

            Assert.IsTrue(QuartoNetProtocol.TryDecodeSnapshot(QuartoNetProtocol.EncodeSnapshot(original), out var decoded));
            Assert.AreEqual(original.Viewer, decoded.Viewer);
            Assert.AreEqual(original.CurrentPlayer, decoded.CurrentPlayer);
            Assert.AreEqual(original.Phase, decoded.Phase);
            Assert.AreEqual(original.PieceInHand, decoded.PieceInHand);
            Assert.AreEqual(original.Ply, decoded.Ply);
            Assert.AreEqual(original.LastMove, decoded.LastMove);
            Assert.AreEqual(original.Result.Status, decoded.Result.Status);
            CollectionAssert.AreEqual(original.State.Cells.ToArray(), decoded.State.Cells.ToArray());
        }

        [Test]
        public void FinishedSnapshot_RoundTripsWithWinningLines()
        {
            var cells = Enumerable.Repeat(QuartoBoard.Empty, 16).ToArray();
            cells[0] = 1;
            cells[1] = 3;
            cells[2] = 5;
            var rules = new QuartoRules();
            var state = rules.ApplyMove(QuartoSetup.CreateCustom(cells, 0, TurnPhase.Place, 7), QuartoMove.Place(3, 0));
            var original = MatchSnapshot.For(state, 1);

            Assert.IsTrue(QuartoNetProtocol.TryDecodeSnapshot(QuartoNetProtocol.EncodeSnapshot(original), out var decoded));
            Assert.AreEqual(GameStatus.Win, decoded.Result.Status);
            Assert.AreEqual(0, decoded.Result.Winner);
            Assert.AreEqual(EndReason.Quarto, decoded.EndReason);
            CollectionAssert.AreEqual(new[] { 0 }, decoded.WinningLines.ToArray());
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[] { QuartoMove.Place(3, 1), QuartoMove.Give(0), QuartoMove.Give(15) })
            {
                Assert.IsTrue(QuartoNetProtocol.TryDecodeMove(QuartoNetProtocol.EncodeMove(m), out var decoded));
                Assert.AreEqual(m, decoded);
            }
        }

        [Test]
        public void MalformedMessages_AreRejected()
        {
            foreach (var bad in new[]
                     {
                         "", "S", "S|9|x", "M|1|1,2", "M|1|a,b,c,d", "M|1|0,4,0,-1", "M|1|1,-1,-1,16", "M|1|2,0,0,0", "X|1",
                         "S|1|0|0|0|-1|0|0|-1|0|-|",
                         // 同じ駒が 2 つ
                         "S|1|1|0|0|-1|2|0|-1|0|-|3,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1",
                         // 置く手番なのに手もちが無い
                         "S|1|1|0|1|-1|1|0|-1|0|-|-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1",
                         // 勝ちと書いてあるのにそろっていない
                         "S|1|1|0|1|-1|2|1|0|1|-|3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1",
                     })
            {
                Assert.IsFalse(QuartoNetProtocol.TryDecodeSnapshot(bad, out _), bad);
                Assert.IsFalse(QuartoNetProtocol.TryDecodeMove(bad, out _), bad);
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
            Assert.AreEqual(TurnPhase.Give, _guest.Snapshot.Phase);
        }

        [Test]
        public void FullGame_BothSidesAgreeOnEveryStep()
        {
            _host.StartNewGame();
            Pump();
            var rng = new Random(8);
            int guard = 0;
            while (!_host.Snapshot.Result.IsOver && guard++ < 100)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                var moves = side.Snapshot.GetLegalMoves();
                Assert.IsNotEmpty(moves);
                side.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                Pump();
                CollectionAssert.AreEqual(_host.Snapshot.State.Cells.ToArray(), _guest.Snapshot.State.Cells.ToArray());
                Assert.AreEqual(_host.Snapshot.PieceInHand, _guest.Snapshot.PieceInHand);
                Assert.AreEqual(_host.Snapshot.Ply, _guest.Snapshot.Ply);
            }
            Assert.IsTrue(_host.Snapshot.Result.IsOver);
            Assert.AreEqual(_host.Snapshot.Result.Status, _guest.Snapshot.Result.Status);
            Assert.AreEqual(_host.Snapshot.Result.Winner, _guest.Snapshot.Result.Winner);
            Assert.AreEqual(_host.Snapshot.EndReason, _guest.Snapshot.EndReason);
            CollectionAssert.AreEqual(_host.Snapshot.WinningLines.ToArray(), _guest.Snapshot.WinningLines.ToArray());
        }

        [Test]
        public void GuestMoveOutOfTurn_IsIgnored()
        {
            _host.StartNewGame();
            Pump();
            if (_host.Snapshot.CurrentPlayer != 0)
            {
                // 先手がゲストなら 1 行動進めてホストの手番にする
                _guest.SubmitLocalMove(_guest.Snapshot.GetLegalMoves()[0]);
                Pump();
            }
            int ply = _host.Snapshot.Ply;
            _guest.SubmitLocalMove(QuartoMove.Give(0));
            _guest.SubmitLocalMove(QuartoMove.Place(0, 0));
            Pump();
            Assert.AreEqual(ply, _host.Snapshot.Ply);
        }

        [Test]
        public void Host_RejectsIllegalMoveFromTamperedGuestAndResyncs()
        {
            _host.StartNewGame();
            Pump();
            // 盤に駒が 1 つ以上あり、ゲストが置く番になるまで進める
            int guard = 0;
            while (!(_host.Snapshot.CurrentPlayer == 1 && _host.Snapshot.Phase == TurnPhase.Place && _host.Snapshot.State.PlacedCount > 0)
                   && guard++ < 10)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                side.SubmitLocalMove(side.Snapshot.GetLegalMoves()[0]);
                Pump();
            }
            Assert.AreEqual(1, _host.Snapshot.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Place, _host.Snapshot.Phase);

            int ply = _host.Snapshot.Ply;
            string error = null;
            _host.ProtocolError += e => error = e;
            int guestUpdates = 0;
            _guest.SnapshotChanged += _ => guestUpdates++;

            // 改造クライアントを想定: 置く番に駒を渡そうとする / 埋まったマスに置こうとする
            _guestLink.Send(QuartoNetProtocol.EncodeMove(QuartoMove.Give(1)));
            int occupied = Array.FindIndex(_host.Snapshot.State.Cells.ToArray(), c => c >= 0);
            _guestLink.Send(QuartoNetProtocol.EncodeMove(QuartoMove.Place(QuartoBoard.X(occupied), QuartoBoard.Y(occupied))));
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
            _hostLink.Send(QuartoNetProtocol.EncodeSnapshot(_host.Snapshot));
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
            Assert.IsTrue(match.Snapshot.IsNewGame);
            int guard = 0;
            while (!match.Snapshot.Result.IsOver && guard++ < 100)
            {
                if (match.IsCpuTurn) match.PlayCpuTurn();
                else
                {
                    var moves = match.Snapshot.GetLegalMoves();
                    match.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                }
            }
            Assert.IsTrue(match.Snapshot.Result.IsOver);
        }

        [Test]
        public void LocalCpuMatch_IgnoresMovesOnCpuTurn()
        {
            var match = new LocalCpuMatch(new Random(2));
            match.StartNewGame();
            if (!match.IsCpuTurn)
            {
                match.SubmitLocalMove(QuartoMove.Give(0));
                Assert.IsTrue(match.IsCpuTurn);
            }
            int ply = match.Snapshot.Ply;
            foreach (var m in new QuartoRules().GetLegalMoves(match.Snapshot.State))
                match.SubmitLocalMove(m);
            Assert.AreEqual(ply, match.Snapshot.Ply);
        }

        // ---------- 補助 ----------

        void PlayRandomGame(Random rng)
        {
            int guard = 0;
            while (!_host.Snapshot.Result.IsOver && guard++ < 100)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                var moves = side.Snapshot.GetLegalMoves();
                Assert.IsNotEmpty(moves);
                side.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                Pump();
            }
            Assert.IsTrue(_host.Snapshot.Result.IsOver);
        }
    }
}
