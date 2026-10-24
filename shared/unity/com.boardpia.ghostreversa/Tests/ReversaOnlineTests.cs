using System;
using System.Linq;
using BoardGames.Core;
using BoardGames.GhostReversa.Online;
using NUnit.Framework;

namespace BoardGames.GhostReversa.Tests
{
    public class ReversaOnlineTests
    {
        readonly ReversaRules _rules = new ReversaRules();

        // ---------- プロトコル ----------

        [Test]
        public void Snapshot_RoundTrips_ForBothViewers_ThroughAWholeGame()
        {
            var rng = new Random(21);
            for (int game = 0; game < 30; game++)
            {
                var s = ReversaSetup.CreateRandom(rng);
                while (true)
                {
                    for (int viewer = 0; viewer < 2; viewer++)
                    {
                        var snap = MatchSnapshot.For(s, viewer);
                        string text = ReversaNetProtocol.EncodeSnapshot(snap);
                        Assert.IsTrue(ReversaNetProtocol.TryDecodeSnapshot(text, out var back), text);
                        Assert.AreEqual(text, ReversaNetProtocol.EncodeSnapshot(back));
                        Assert.AreEqual(snap.Result.ToString(), back.Result.ToString());
                        Assert.AreEqual(snap.EndReason, back.EndReason);
                        CollectionAssert.AreEquivalent(snap.View.GetLegalMoves(), back.View.GetLegalMoves());
                    }
                    if (s.Result.IsOver) break;
                    var moves = _rules.GetLegalMoves(s);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
            }
        }

        [Test]
        public void Move_RoundTrips()
        {
            var moves = new[]
            {
                ReversaMove.Discard(TileKind.Blue), ReversaMove.Discard(TileKind.Red), ReversaMove.Pass(),
                ReversaMove.Place(TileKind.Blue, 0, 2, true), ReversaMove.Place(TileKind.Red, 2, 0, false),
                ReversaMove.Place(TileKind.Poltergeist, 1, 1, true),
            };
            foreach (var m in moves)
            {
                Assert.IsTrue(ReversaNetProtocol.TryDecodeMove(ReversaNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
        }

        [TestCase("")]
        [TestCase("M|1|")]
        [TestCase("M|2|1,1,0,0,1")]
        [TestCase("M|1|1,1,3,0,1")]
        [TestCase("M|1|1,9,0,0,1")]
        [TestCase("M|1|1,1,0,0,2")]
        [TestCase("M|1|7,1,0,0,1")]
        [TestCase("X|1|1,1,0,0,1")]
        public void BrokenMoves_AreRejected(string message)
        {
            Assert.IsFalse(ReversaNetProtocol.TryDecodeMove(message, out _));
        }

        [Test]
        public void BrokenSnapshots_AreRejected()
        {
            string good = ReversaNetProtocol.EncodeSnapshot(MatchSnapshot.For(ReversaSetup.Create(0), 0));
            Assert.IsTrue(ReversaNetProtocol.TryDecodeSnapshot(good, out _));
            Assert.IsFalse(ReversaNetProtocol.TryDecodeSnapshot(good.Replace("S|1|0", "S|1|5"), out _), "席が不正");
            Assert.IsFalse(ReversaNetProtocol.TryDecodeSnapshot(good + "|x", out _), "項目が多い");
            Assert.IsFalse(ReversaNetProtocol.TryDecodeSnapshot(good.Replace("-;-;-", "-;-"), out _), "マスが足りない");
            Assert.IsFalse(ReversaNetProtocol.TryDecodeSnapshot(good.Replace("|-;", "|0,9,1;"), out _), "駒の種類が不正");
            Assert.IsFalse(ReversaNetProtocol.TryDecodeSnapshot("garbage", out _));
        }

        [Test]
        public void Snapshot_DoesNotLeakHiddenInformation()
        {
            var rng = new Random(8);
            for (int game = 0; game < 30; game++)
            {
                var s = ReversaSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    for (int viewer = 0; viewer < 2; viewer++)
                    {
                        int opp = 1 - viewer;
                        ReversaNetProtocol.TryDecodeSnapshot(ReversaNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, viewer)), out var seen);
                        Assert.AreEqual(TileKind.Unknown, seen.View.DiscardOf(opp));
                        Assert.AreEqual(ReversaPlayerView.Hidden, seen.View.HandOf(opp, TileKind.Blue));
                        Assert.IsFalse(seen.View.Cells.Any(c => c.HasValue && c.Value.Owner == opp && !c.Value.FaceUp &&
                                                                c.Value.Kind != TileKind.Unknown));
                    }
                    var moves = _rules.GetLegalMoves(s);
                    s = _rules.ApplyMove(s, moves[rng.Next(moves.Count)]);
                }
            }
        }

        // ---------- OnlineMatch(ホスト権威) ----------

        [Test]
        public void HostAndGuest_PlayAWholeGame_InSync()
        {
            LoopbackTransport.CreatePair(out var hostLink, out var guestLink);
            var host = new OnlineMatch(hostLink, true, new Random(1));
            var guest = new OnlineMatch(guestLink, false);
            host.StartNewGame();
            hostLink.Pump();
            Assert.IsNotNull(guest.Snapshot);
            Assert.AreEqual(1, guest.LocalPlayer);

            var rng = new Random(2);
            int guard = 0;
            while (!host.Snapshot.Result.IsOver)
            {
                var mover = host.Snapshot.CurrentPlayer == 0 ? host : guest;
                var moves = mover.Snapshot.View.GetLegalMoves();
                mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                hostLink.Pump();
                Assert.AreEqual(host.Snapshot.Ply, guest.Snapshot.Ply);
                Assert.Less(++guard, 200);
            }
            Assert.IsTrue(guest.Snapshot.Result.IsOver);
            Assert.AreEqual(host.Snapshot.Result.ToString(), guest.Snapshot.Result.ToString());
            // 決着後は全公開
            Assert.AreNotEqual(TileKind.Unknown, guest.Snapshot.View.DiscardOf(0));
        }

        [Test]
        public void GuestNeverReceivesTheHostsHiddenInformation()
        {
            LoopbackTransport.CreatePair(out var hostLink, out var guestLink);
            var host = new OnlineMatch(hostLink, true, new Random(3));
            var guest = new OnlineMatch(guestLink, false);
            host.StartNewGame();
            hostLink.Pump();
            var rng = new Random(4);
            while (!host.Snapshot.Result.IsOver)
            {
                var mover = host.Snapshot.CurrentPlayer == 0 ? host : guest;
                var moves = mover.Snapshot.View.GetLegalMoves();
                mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                hostLink.Pump();
            }
            foreach (var message in hostLink.SentLog.Where(m => m.StartsWith("S|")))
            {
                Assert.IsTrue(ReversaNetProtocol.TryDecodeSnapshot(message, out var snap));
                if (snap.Result.IsOver) continue;
                Assert.AreEqual(TileKind.Unknown, snap.View.DiscardOf(0));
                Assert.AreEqual(ReversaPlayerView.Hidden, snap.View.HandOf(0, TileKind.Red));
            }
        }

        [Test]
        public void HostRejectsIllegalGuestMove_AndResyncs()
        {
            LoopbackTransport.CreatePair(out var hostLink, out var guestLink);
            var host = new OnlineMatch(hostLink, true, new Random(5));
            var guest = new OnlineMatch(guestLink, false);
            string error = null;
            host.ProtocolError += e => error = e;
            host.StartNewGame();
            hostLink.Pump();
            int plyBefore = host.Snapshot.Ply;
            guestLink.Send(ReversaNetProtocol.EncodeMove(ReversaMove.Place(TileKind.Blue, 0, 0, true)));
            hostLink.Pump();
            Assert.IsNotNull(error);
            Assert.AreEqual(plyBefore, host.Snapshot.Ply);
            Assert.AreEqual(plyBefore, guest.Snapshot.Ply);
        }

        [Test]
        public void Rematch_StartsANewGame_WhenBothAsk()
        {
            LoopbackTransport.CreatePair(out var hostLink, out var guestLink);
            var host = new OnlineMatch(hostLink, true, new Random(6));
            var guest = new OnlineMatch(guestLink, false);
            host.StartNewGame();
            hostLink.Pump();
            var rng = new Random(7);
            while (!host.Snapshot.Result.IsOver)
            {
                var mover = host.Snapshot.CurrentPlayer == 0 ? host : guest;
                var moves = mover.Snapshot.View.GetLegalMoves();
                mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                hostLink.Pump();
            }
            guest.RequestRematch();
            hostLink.Pump();
            Assert.IsTrue(host.OpponentWantsRematch);
            Assert.IsTrue(host.Snapshot.Result.IsOver);
            host.RequestRematch();
            hostLink.Pump();
            Assert.IsTrue(host.Snapshot.IsNewGame);
            Assert.IsTrue(guest.Snapshot.IsNewGame);
            Assert.IsFalse(guest.LocalWantsRematch);
        }

        [Test]
        public void ServerSeatGuest_UsesTheGivenSeat()
        {
            LoopbackTransport.CreatePair(out var serverLink, out var clientLink);
            var client = new OnlineMatch(clientLink, 0);
            Assert.AreEqual(0, client.LocalPlayer);
            var state = ReversaSetup.Create(0);
            serverLink.Send(ReversaNetProtocol.EncodeSnapshot(MatchSnapshot.For(state, 0)));
            serverLink.Pump();
            Assert.IsNotNull(client.Snapshot);
            client.SubmitLocalMove(ReversaMove.Discard(TileKind.Red));
            serverLink.Pump();
            Assert.AreEqual(ReversaNetProtocol.EncodeMove(ReversaMove.Discard(TileKind.Red)), clientLink.SentLog.Last());
        }
    }
}
