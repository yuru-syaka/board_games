using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.GhostSecret.Online;
using NUnit.Framework;

namespace BoardGames.GhostSecret.Tests
{
    public class SecretOnlineTests
    {
        readonly SecretRules _rules = new SecretRules();

        /// <summary>局面を文字列にして戻すと、見えている情報がすべて同じになる(途中の局面も、終局も)。</summary>
        [Test]
        public void Snapshot_RoundTripsThroughTheProtocol([Values(2, 3, 4)] int players)
        {
            var s = SecretSetup.CreateRandom(new Random(players), players);
            var cpus = Enumerable.Range(0, players).Select(i => new SecretCpu(new Random(i))).ToArray();
            int checks = 0;
            while (true)
            {
                for (int viewer = 0; viewer < players; viewer++)
                {
                    var original = MatchSnapshot.For(s, viewer);
                    string text = SecretNetProtocol.EncodeSnapshot(original);
                    Assert.IsTrue(SecretNetProtocol.TryDecodeSnapshot(text, out var decoded), text);
                    Assert.AreEqual(text, SecretNetProtocol.EncodeSnapshot(decoded), "文字列に戻すと同じ");
                    AssertSameView(original.View, decoded.View);
                    checks++;
                }
                if (s.Result.IsOver) break;
                s = _rules.ApplyMove(s, cpus[s.CurrentPlayer].ChooseMove(SecretPlayerView.Create(s, s.CurrentPlayer)));
            }
            Assert.Greater(checks, players * 10);
        }

        static void AssertSameView(SecretPlayerView a, SecretPlayerView b)
        {
            Assert.AreEqual(a.Viewer, b.Viewer);
            Assert.AreEqual(a.CurrentPlayer, b.CurrentPlayer);
            Assert.AreEqual(a.Phase, b.Phase);
            Assert.AreEqual(a.Round, b.Round);
            Assert.AreEqual(a.DeckCount, b.DeckCount);
            Assert.AreEqual(a.HasSetAside, b.HasSetAside);
            Assert.AreEqual(a.Result.ToString(), b.Result.ToString());
            CollectionAssert.AreEqual(a.FaceUpAside, b.FaceUpAside);
            CollectionAssert.AreEqual(a.Peek, b.Peek);
            for (int p = 0; p < a.PlayerCount; p++)
            {
                CollectionAssert.AreEqual(a.PlayerAt(p).Hand, b.PlayerAt(p).Hand);
                CollectionAssert.AreEqual(a.PlayedBy(p), b.PlayedBy(p));
                Assert.AreEqual(a.Tokens(p), b.Tokens(p));
                Assert.AreEqual(a.IsOut(p), b.IsOut(p));
                Assert.AreEqual(a.IsProtected(p), b.IsProtected(p));
                Assert.AreEqual(a.KnownHandOf(p), b.KnownHandOf(p));
            }
            CollectionAssert.AreEqual(a.GetLegalMoves(), b.GetLegalMoves(), "届いた局面でも同じ手が打てる");
            Assert.AreEqual(a.Events.Count, b.Events.Count);
        }

        [Test]
        public void Snapshot_DoesNotLeakHiddenCards()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var s = SecretSetup.CreateRandom(new Random(seed), 3);
                var cpu = new SecretCpu(new Random(seed));
                for (int i = 0; i < 6 && !s.Result.IsOver; i++)
                {
                    for (int viewer = 0; viewer < 3; viewer++)
                    {
                        Assert.IsTrue(SecretNetProtocol.TryDecodeSnapshot(SecretNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, viewer)), out var seen));
                        var v = seen.View;
                        for (int p = 0; p < 3; p++)
                            if (p != viewer) Assert.IsTrue(v.PlayerAt(p).Hand.All(c => c == -1), "ほかの人の手札は届かない");
                        Assert.IsTrue(v.Events.All(e => e.VisibleTo(viewer) || (e.Card == -1 && e.Card2 == -1)), "見えない出来事のカードは届かない");
                        if (v.Phase == SecretPhase.Fortune && v.CurrentPlayer != viewer) Assert.IsTrue(v.Peek.All(c => c == -1));
                    }
                    s = _rules.ApplyMove(s, cpu.ChooseMove(SecretPlayerView.Create(s, s.CurrentPlayer)));
                }
            }
        }

        [Test]
        public void Moves_RoundTrip()
        {
            var moves = new[]
            {
                SecretMove.Play(3, 1, Ghost.Lamp), SecretMove.Play(12), SecretMove.Play(14, 0), SecretMove.FortuneOrder(true),
                SecretMove.FortuneOrder(false), SecretMove.LampSwap(true), SecretMove.LampSwap(false),
            };
            foreach (var m in moves)
            {
                Assert.IsTrue(SecretNetProtocol.TryDecodeMove(SecretNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
        }

        [Test]
        public void BrokenMessages_AreRejected()
        {
            Assert.IsFalse(SecretNetProtocol.TryDecodeSnapshot("S|1|0|2", out _));
            Assert.IsFalse(SecretNetProtocol.TryDecodeMove("M|1|0,1", out _));
            Assert.IsFalse(SecretNetProtocol.TryDecodeMove("M|2|0,1,1,1,0", out _));
            Assert.AreEqual(NetMessageKind.Invalid, SecretNetProtocol.KindOf("X|1"));
            var s = SecretSetup.CreateRandom(new Random(1), 2);
            string ok = SecretNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 0));
            Assert.IsTrue(SecretNetProtocol.TryDecodeSnapshot(ok, out _));
            Assert.IsFalse(SecretNetProtocol.TryDecodeSnapshot(ok.Replace("S|1|0|2|", "S|1|0|9|"), out _), "人数が範囲外");
        }

        /// <summary>サーバーの部屋: 全席が CPU と同じ考え方で指して終局し、途中で CPU に代わった席は最下位になる。</summary>
        [Test]
        public void Table_PlaysToTheEndWithATakeover([Values(2, 3, 4)] int players)
        {
            var table = new SecretTable(players, 42 + players);
            var cpu = new SecretCpu(new Random(9));
            bool tookOver = false;
            int guard = 0;
            while (!table.IsOver)
            {
                Assert.Less(guard++, 5000);
                int seat = table.CurrentSeat;
                if (!tookOver && players > 2 && table.State.Round >= 2 && seat == players - 1)
                {
                    Assert.IsTrue(table.Takeover(seat));
                    tookOver = true;
                    continue;
                }
                Assert.IsTrue(table.TryGetAutoMove(seat, out var auto));
                var move = cpu.ChooseMove(SecretPlayerView.Create(table.State, seat));
                Assert.IsTrue(table.TryApply(seat, guard % 2 == 0 ? move : auto));
            }
            var ranks = table.Ranks;
            if (tookOver) Assert.AreEqual(players - 1, ranks[players - 1], "抜けた席は最下位");
            Assert.IsTrue(table.SeatResult.IsOver);
        }

        [Test]
        public void Table_RejectsMovesOutOfTurn()
        {
            var table = new SecretTable(3, 7);
            int seat = table.CurrentSeat;
            int other = (seat + 1) % 3;
            var move = new SecretCpu(new Random(1)).ChooseMove(SecretPlayerView.Create(table.State, seat));
            Assert.IsFalse(table.TryApply(other, move));
            Assert.IsTrue(table.TryApply(seat, move));
        }

        /// <summary>同じ種と手の一覧から作り直すと、同じ局面になる(サーバーは毎回作り直す)。</summary>
        [Test]
        public void Table_IsDeterministicFromSeedAndMoves()
        {
            var a = new SecretTable(3, 99);
            var moves = new List<(int seat, SecretMove move)>();
            var cpu = new SecretCpu(new Random(4));
            for (int i = 0; i < 40 && !a.IsOver; i++)
            {
                int seat = a.CurrentSeat;
                var m = cpu.ChooseMove(SecretPlayerView.Create(a.State, seat));
                Assert.IsTrue(a.TryApply(seat, m));
                moves.Add((seat, m));
            }
            var b = new SecretTable(3, 99);
            foreach (var (seat, move) in moves) Assert.IsTrue(b.TryApply(seat, move));
            for (int viewer = 0; viewer < 3; viewer++) Assert.AreEqual(a.SnapshotFor(viewer), b.SnapshotFor(viewer));
        }

        sealed class Loopback : IMatchTransport
        {
            public readonly List<string> Sent = new List<string>();
            public event Action<string> MessageReceived;
            public void Send(string message) => Sent.Add(message);
            public void Deliver(string message) => MessageReceived?.Invoke(message);
        }

        [Test]
        public void OnlineMatch_SendsOnlyLegalMovesAndRematch()
        {
            var table = new SecretTable(2, 3);
            int seat = table.CurrentSeat;
            var link = new Loopback();
            var match = new OnlineMatch(link, seat);
            link.Deliver(table.SnapshotFor(seat));
            Assert.IsTrue(match.Snapshot.View.IsMyTurn);
            match.SubmitLocalMove(SecretMove.LampSwap(true)); // 今は出す番なので送らない
            Assert.AreEqual(0, link.Sent.Count);
            var legal = match.Snapshot.View.GetLegalMoves()[0];
            match.SubmitLocalMove(legal);
            Assert.AreEqual(1, link.Sent.Count);
            match.RequestRematch(); // 終局前は送らない
            Assert.AreEqual(1, link.Sent.Count);
        }
    }
}
