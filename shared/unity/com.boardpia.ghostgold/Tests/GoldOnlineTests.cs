using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.GhostGold.Online;
using NUnit.Framework;

namespace BoardGames.GhostGold.Tests
{
    public class GoldOnlineTests
    {
        readonly GoldRules _rules = new GoldRules();

        /// <summary>局面を文字列にして戻すと、見えている情報がすべて同じになる(途中の局面も、終局も)。</summary>
        [Test]
        public void Snapshot_RoundTripsThroughTheProtocol([Values(2, 3, 5)] int players)
        {
            var s = GoldSetup.Create(players, 10 + players);
            var cpus = Enumerable.Range(0, players).Select(i => new GoldCpu(new Random(i))).ToArray();
            int checks = 0;
            while (true)
            {
                for (int viewer = 0; viewer < players; viewer++)
                {
                    var original = MatchSnapshot.For(s, viewer);
                    string text = GoldNetProtocol.EncodeSnapshot(original);
                    Assert.IsTrue(GoldNetProtocol.TryDecodeSnapshot(text, out var decoded), text);
                    Assert.AreEqual(text, GoldNetProtocol.EncodeSnapshot(decoded), "文字列に戻すと同じ");
                    AssertSameView(original.View, decoded.View);
                    checks++;
                }
                if (s.Result.IsOver) break;
                s = _rules.ApplyMove(s, cpus[s.CurrentPlayer].ChooseMove(GoldPlayerView.Create(s, s.CurrentPlayer)));
            }
            Assert.Greater(checks, players * 10);
        }

        static void AssertSameView(GoldPlayerView a, GoldPlayerView b)
        {
            Assert.AreEqual(a.Viewer, b.Viewer);
            Assert.AreEqual(a.CurrentPlayer, b.CurrentPlayer);
            Assert.AreEqual(a.Phase, b.Phase);
            Assert.AreEqual(a.Night, b.Night);
            Assert.AreEqual(a.DeckCount, b.DeckCount);
            Assert.AreEqual(a.Leftover, b.Leftover);
            Assert.AreEqual(a.Result.ToString(), b.Result.ToString());
            CollectionAssert.AreEqual(a.Path, b.Path);
            for (int k = 0; k < GoldLogic.HazardKinds; k++) Assert.AreEqual(a.HazardRemoved(k), b.HazardRemoved(k));
            for (int n = 1; n <= GoldLogic.RelicCount; n++) Assert.AreEqual(a.RelicStatusOf(n), b.RelicStatusOf(n));
            for (int p = 0; p < a.PlayerCount; p++)
            {
                Assert.AreEqual(a.IsExploring(p), b.IsExploring(p));
                Assert.AreEqual(a.Carry(p), b.Carry(p));
                Assert.AreEqual(a.Safe(p), b.Safe(p));
                CollectionAssert.AreEqual(a.RelicsOf(p), b.RelicsOf(p));
                Assert.AreEqual(a.HasAmulet(p), b.HasAmulet(p));
                Assert.AreEqual(a.HasDecided(p), b.HasDecided(p));
            }
            Assert.AreEqual(a.MyChoice, b.MyChoice);
            CollectionAssert.AreEqual(a.GetLegalMoves(), b.GetLegalMoves(), "届いた局面でも同じ手が打てる");
            Assert.AreEqual(a.Events.Count, b.Events.Count);
        }

        [Test]
        public void Snapshot_DoesNotLeakTheDeckOrOtherPlayersChoices()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var s = GoldSetup.Create(4, seed);
                var cpu = new GoldCpu(new Random(seed));
                for (int i = 0; i < 12 && !s.Result.IsOver; i++)
                {
                    for (int viewer = 0; viewer < 4; viewer++)
                    {
                        string text = GoldNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, viewer));
                        Assert.IsTrue(GoldNetProtocol.TryDecodeSnapshot(text, out var seen));
                        var m = seen.View.Masked;
                        Assert.IsTrue(m.Deck.All(c => c == -1), "山札の順番は届かない");
                        for (int p = 0; p < 4; p++)
                            if (p != viewer) Assert.AreEqual(Choice.None, m.Players[p].Choice, "ほかの人の決定は届かない");
                    }
                    s = _rules.ApplyMove(s, cpu.ChooseMove(GoldPlayerView.Create(s, s.CurrentPlayer)));
                }
            }
        }

        [Test]
        public void Moves_RoundTrip()
        {
            foreach (var m in new[] { GoldMove.Go, GoldMove.Return })
            {
                Assert.IsTrue(GoldNetProtocol.TryDecodeMove(GoldNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
        }

        [Test]
        public void BrokenMessages_AreRejected()
        {
            Assert.IsFalse(GoldNetProtocol.TryDecodeSnapshot("S|1|0|2", out _));
            Assert.IsFalse(GoldNetProtocol.TryDecodeMove("M|1|2", out _));
            Assert.IsFalse(GoldNetProtocol.TryDecodeMove("M|1|-1", out _));
            Assert.IsFalse(GoldNetProtocol.TryDecodeMove("M|2|0", out _));
            Assert.AreEqual(NetMessageKind.Invalid, GoldNetProtocol.KindOf("X|1"));
            var s = GoldSetup.Create(2, 1);
            string ok = GoldNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 0));
            Assert.IsTrue(GoldNetProtocol.TryDecodeSnapshot(ok, out _));
            Assert.IsFalse(GoldNetProtocol.TryDecodeSnapshot(ok.Replace("S|1|0|2|", "S|1|0|9|"), out _), "人数が範囲外");

            // ほかの人の決定が入った局面は受け取らない
            s = _rules.ApplyMove(s, GoldMove.Return);
            string leak = GoldNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 0));
            var parts = leak.Split('|');
            var players = parts[15].Split(';');
            Assert.IsTrue(players[0].EndsWith(":1:1"), players[0]); // 自分の決定(帰る)は自分には届く
            parts[2] = "1"; // 席 1 として受け取ると、席 0 の決定が見えてしまう
            Assert.IsFalse(GoldNetProtocol.TryDecodeSnapshot(string.Join("|", parts), out _));
        }

        /// <summary>サーバーの部屋: 全席が CPU と同じ考え方で決めて終局し、途中で CPU に代わった席は最下位になる。</summary>
        [Test]
        public void Table_PlaysToTheEndWithATakeover([Values(2, 3, 5)] int players)
        {
            var table = new GoldTable(players, 42 + players);
            var cpu = new GoldCpu(new Random(9));
            bool tookOver = false;
            int guard = 0;
            while (!table.IsOver)
            {
                Assert.Less(guard++, 5000);
                int seat = table.CurrentSeat;
                if (!tookOver && players > 2 && table.State.Night >= 2 && seat == players - 1)
                {
                    Assert.IsTrue(table.Takeover(seat));
                    tookOver = true;
                    continue;
                }
                Assert.IsTrue(table.TryGetAutoMove(seat, out var auto));
                var move = cpu.ChooseMove(GoldPlayerView.Create(table.State, seat));
                Assert.IsTrue(table.TryApply(seat, guard % 2 == 0 ? move : auto));
            }
            var ranks = table.Ranks;
            if (tookOver) Assert.AreEqual(players - 1, ranks[players - 1], "抜けた席は最下位");
            Assert.IsTrue(table.SeatResult.IsOver);
        }

        [Test]
        public void Table_RejectsMovesOutOfTurn()
        {
            var table = new GoldTable(3, 7);
            int seat = table.CurrentSeat;
            int other = (seat + 1) % 3;
            Assert.IsFalse(table.TryApply(other, GoldMove.Go));
            Assert.IsFalse(table.TryApply(seat, GoldMove.Create(Choice.None)));
            Assert.IsTrue(table.TryApply(seat, GoldMove.Go));
        }

        /// <summary>同じ種と手の一覧から作り直すと、同じ局面になる(サーバーは毎回作り直す)。</summary>
        [Test]
        public void Table_IsDeterministicFromSeedAndMoves()
        {
            var a = new GoldTable(3, 99);
            var moves = new List<(int seat, GoldMove move)>();
            var cpu = new GoldCpu(new Random(4));
            for (int i = 0; i < 40 && !a.IsOver; i++)
            {
                int seat = a.CurrentSeat;
                var m = cpu.ChooseMove(GoldPlayerView.Create(a.State, seat));
                Assert.IsTrue(a.TryApply(seat, m));
                moves.Add((seat, m));
            }
            var b = new GoldTable(3, 99);
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
            var table = new GoldTable(2, 3);
            var link = new Loopback();
            var match = new OnlineMatch(link, 1);
            link.Deliver(table.SnapshotFor(1));
            Assert.IsFalse(match.Snapshot.View.IsMyTurn); // 席 0 から決める
            match.SubmitLocalMove(GoldMove.Go);
            Assert.AreEqual(0, link.Sent.Count);

            Assert.IsTrue(table.TryApply(0, GoldMove.Go));
            link.Deliver(table.SnapshotFor(1));
            Assert.IsTrue(match.Snapshot.View.IsMyTurn);
            match.SubmitLocalMove(GoldMove.Return);
            CollectionAssert.AreEqual(new[] { "M|1|1" }, link.Sent);
            match.RequestRematch(); // 終局前は送らない
            Assert.AreEqual(1, link.Sent.Count);
        }
    }
}
